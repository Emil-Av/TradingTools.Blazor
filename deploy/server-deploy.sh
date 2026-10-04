#!/usr/bin/env bash
# Runs on the VPS (uploaded and started by deploy.ps1). Installs one uploaded build as a new release and
# switches to it:
#
#   /var/www/tradingtools-data/              live data, NEVER written by a deploy
#       Screenshots/                         the screenshots (wwwroot/Screenshots of every release links here)
#       appsettings.Production.json          production connection string (only readable by root)
#   /var/www/tradingtools-blazor/
#       releases/<stamp>/                    one folder per deploy; the 3 newest are kept
#       current -> releases/<stamp>          what the service runs
#
# Before switching it dumps the database (the new version may migrate it on start). If the new version
# doesn't come up, the previous release is switched back in.
set -euo pipefail

ARCHIVE="$1"
STAMP="$2"

DATA=/var/www/tradingtools-data
APP=/var/www/tradingtools-blazor
RELEASE="$APP/releases/$STAMP"
SERVICE=tradingtools-blazor
PORT=5001
BACKUPS=/root/backups/deploys
KEEP_RELEASES=3
KEEP_DUMPS=10

fail() { echo "DEPLOY FAILED: $*" >&2; exit 1; }

# --- The live data must be there, and must not be part of the upload -------------------------------
[ -d "$DATA/Screenshots" ] || fail "$DATA/Screenshots is missing - run the one-time setup (docs/DEPLOYMENT.md) first."
[ -f "$DATA/appsettings.Production.json" ] || fail "$DATA/appsettings.Production.json is missing - run the one-time setup first."
[ -f "/etc/systemd/system/$SERVICE.service" ] || fail "the $SERVICE service isn't installed - run the one-time setup first."
[ -e "$RELEASE" ] && fail "$RELEASE already exists."

mkdir -p "$RELEASE"
tar -xzf "$ARCHIVE" -C "$RELEASE"
rm -f "$ARCHIVE"

for path in wwwroot/Screenshots wwwroot/ScreenshotsDev appsettings.Production.json; do
    if [ -e "$RELEASE/$path" ]; then
        rm -rf "$RELEASE"
        fail "the upload contains $path - it must come from the server's data folder, not from the build."
    fi
done

ln -s "$DATA/Screenshots" "$RELEASE/wwwroot/Screenshots"
ln -s "$DATA/appsettings.Production.json" "$RELEASE/appsettings.Production.json"
echo "Release $STAMP unpacked ($(du -sh "$RELEASE" | cut -f1))."

# --- Database safety copy (the app applies pending migrations when it starts) -----------------------
mkdir -p "$BACKUPS" && chmod 700 /root/backups "$BACKUPS"
DUMP="$BACKUPS/TradingTools-before-$STAMP.dump"
(cd /tmp && sudo -u postgres pg_dump --format=custom TradingTools) > "$DUMP"
echo "Database dumped to $DUMP ($(du -h "$DUMP" | cut -f1))."
ls -1t "$BACKUPS"/TradingTools-before-*.dump | tail -n +$((KEEP_DUMPS + 1)) | xargs -r rm -f

# --- Switch -------------------------------------------------------------------------------------------
PREVIOUS=$(readlink -f "$APP/current" 2>/dev/null || true)
ln -sfn "$RELEASE" "$APP/current.new" && mv -Tf "$APP/current.new" "$APP/current"
systemctl restart "$SERVICE"

healthy=false
for _ in $(seq 1 30); do
    sleep 2
    code=$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$PORT/account/login" || true)
    if [ "$code" = "200" ]; then healthy=true; break; fi
done

if ! $healthy; then
    echo "The new version didn't answer on port $PORT. Last log lines:" >&2
    journalctl -u "$SERVICE" -n 40 --no-pager >&2 || true
    if [ -n "$PREVIOUS" ] && [ -d "$PREVIOUS" ]; then
        ln -sfn "$PREVIOUS" "$APP/current.new" && mv -Tf "$APP/current.new" "$APP/current"
        systemctl restart "$SERVICE"
        fail "switched back to $(basename "$PREVIOUS"). The database dump from before this deploy is $DUMP."
    fi
    fail "there's no previous release to switch back to. The database dump from before this deploy is $DUMP."
fi
echo "Release $STAMP is running."

# --- Keep the newest releases (only release folders - the data folder is elsewhere) -----------------
ls -1dt "$APP"/releases/*/ | tail -n +$((KEEP_RELEASES + 1)) | while read -r old; do
    [ "$(readlink -f "$old")" = "$(readlink -f "$APP/current")" ] && continue
    rm -rf -- "$old"
done
echo "Releases on the server: $(ls -1 "$APP/releases" | tr '\n' ' ')"
