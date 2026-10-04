# Deploying to the VPS

The live app runs at **https://e-tradingtools.de** on the VPS `root@217.154.9.117` (Ubuntu 24.04).

```powershell
.\deploy.ps1
```

That's all a deploy is. It builds the app, uploads it and switches the site over, and does **not**
upload screenshots or touch any database - the live ones are on the VPS and stay there. Your local
screenshots and local database are never read.

## How it's laid out on the VPS

| Path | What |
|------|------|
| `/var/www/tradingtools-data/Screenshots/` | **Live screenshots.** Never written by a deploy. |
| `/var/www/tradingtools-data/appsettings.Production.json` | Production connection string (root-only). Never written by a deploy. |
| `/var/www/tradingtools-blazor/releases/<date-time>/` | One folder per deploy; the 3 newest are kept. |
| `/var/www/tradingtools-blazor/current` | Link to the release that runs. |
| `/root/backups/deploys/` | A database dump taken before every deploy; the 10 newest are kept. |
| PostgreSQL database `TradingTools` | **Live database.** Only the app itself changes it (migrations run when it starts). |

Each release's `wwwroot/Screenshots` and `appsettings.Production.json` are links into
`/var/www/tradingtools-data`, so a release folder can be deleted without touching any data.

- Service: `tradingtools-blazor` (systemd, `deploy/tradingtools-blazor.service`), port 5001 on localhost.
- nginx: `/etc/nginx/sites-available/tradingtools` (`deploy/nginx-tradingtools.conf`): HTTPS with the
  Let's Encrypt certificate (certbot renews it automatically), HTTP redirected to HTTPS, WebSockets for
  Blazor, 2 GB uploads for the Backup page.
- The old Razor Pages app (`tradingtools` service, `/var/www/tradingtools`) is stopped. Its
  `wwwroot/Screenshots` is now a link to the same data folder.

## What a deploy does

1. Checks that your SSH key logs in (a password is never used).
2. `dotnet publish` (Release, linux-x64). Fails if the build contains screenshots, production settings,
   or data files; `wwwroot/Screenshots` is excluded in the project file.
3. Uploads it as a new release folder and links in the live screenshots and settings.
4. Dumps the database to `/root/backups/deploys/` (the new version may migrate it on start).
5. Switches `current`, restarts the service and waits for it to answer.
   **If it doesn't come up, the previous release is switched back** and the log is shown.
6. Checks https://e-tradingtools.de.

## SSH key (no password)

Deploys log in with your key `C:\Users\emila\.ssh\id_ed25519`. It's installed on the VPS in
`/root/.ssh/authorized_keys`. On a new machine, create a key (`ssh-keygen -t ed25519`) and install it
once - this is the only time the root password is needed:

```powershell
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh root@217.154.9.117 "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
```

## Useful commands

```powershell
ssh root@217.154.9.117 "journalctl -u tradingtools-blazor -f"          # live log
ssh root@217.154.9.117 "systemctl restart tradingtools-blazor"         # restart
ssh root@217.154.9.117 "ls -l /var/www/tradingtools-blazor/releases"   # releases on the server
```

### Going back to the previous release

```bash
ssh root@217.154.9.117 "cd /var/www/tradingtools-blazor && ls -1t releases | sed -n 2p | xargs -I{} ln -sfn releases/{} current && systemctl restart tradingtools-blazor"
```

If that release is older than a database migration, restore the matching dump from
`/root/backups/deploys/` too (or use the Backup page).

## One-time server setup (already done on 30 Sep 2026)

For reference, or for a new server:

1. `mkdir -p /var/www/tradingtools-data` and put the screenshots in `Screenshots/` and the production
   settings in `appsettings.Production.json` (`chmod 600`).
2. Copy `deploy/tradingtools-blazor.service` to `/etc/systemd/system/`, then
   `systemctl daemon-reload && systemctl enable tradingtools-blazor`.
3. Run `.\deploy.ps1` once.
4. Install `deploy/nginx-tradingtools.conf` as `/etc/nginx/sites-available/tradingtools`, then
   `nginx -t && systemctl reload nginx`. For a new domain, get the certificate first with
   `certbot --nginx -d <domain>`.
5. The server needs the .NET 9 ASP.NET Core runtime and the PostgreSQL client tools (see VPS-SETUP.md).
