# VPS setup for Backup (export / import)

The **Backup** page (`/backup`) exports and imports the database with the PostgreSQL client tools
(`pg_dump`, `pg_restore`, `psql`). The app runs them as separate programs, so they must be installed
on the server that runs the app. Screenshot export/import needs nothing extra.

Locally (Windows) nothing needs installing: the app finds the tools of the PostgreSQL server that's
already installed (`C:\Program Files\PostgreSQL\18\bin`).

## 1. Install the PostgreSQL client tools

`pg_dump` must be **the same major version as the database server, or newer** - an older `pg_dump`
refuses to dump a newer server. Check the server version first:

```bash
sudo -u postgres psql -c "SHOW server_version;"
```

(or `psql -h <host> -U <user> -d TradingTools -c "SHOW server_version;"` if the database is elsewhere).

### Debian / Ubuntu

The distribution's `postgresql-client` package may be older than your server. The PostgreSQL apt
repository has every version:

```bash
sudo apt install -y postgresql-common
sudo /usr/share/postgresql-common/pgdg/apt.postgresql.org.sh    # adds the PostgreSQL apt repository
sudo apt update
sudo apt install -y postgresql-client-18                         # use your server's major version
```

If the database server runs on the same VPS, the client tools are already installed with it - skip this.

### Check

```bash
pg_dump --version
pg_restore --version
psql --version
```

All three should print the same major version as the server (or newer).

## 2. Tell the app where the tools are (only if needed)

The app looks for the tools on the `PATH` of the user it runs as. `apt` usually links them into
`/usr/bin`, so nothing else is needed. If `which pg_dump` finds nothing for that user, or it finds an
older version, point the app at the right folder in `appsettings.Production.json`:

```json
{
  "Backup": {
    "PostgresBinPath": "/usr/lib/postgresql/18/bin"
  }
}
```

or as an environment variable of the service: `Backup__PostgresBinPath=/usr/lib/postgresql/18/bin`.

## 3. Allow large uploads through the reverse proxy

The app itself has no upload limit on the import endpoints, but **nginx allows only 1 MB per request
by default**, and the screenshots zip is hundreds of MB. In the `server` block that proxies to the app:

```nginx
location /backup/ {
    client_max_body_size 2G;        # larger than your biggest screenshots zip
    proxy_request_buffering off;    # stream the upload to the app instead of buffering it on disk first
    proxy_read_timeout 600s;        # a large import can take a few minutes
    proxy_send_timeout 600s;
    proxy_pass http://127.0.0.1:5000;   # same target as your main location block
    # plus the same proxy_set_header lines as your main location block
}
```

Then `sudo nginx -t && sudo systemctl reload nginx`.

## 4. Disk space and permissions

- **Temp space**: exports are written to the system temp folder (`/tmp`) before downloading, and uploads
  are buffered there before importing. Keep free space of at least **twice the size of the screenshots
  folder**.
- **Screenshots folder**: the user the app runs as must be able to write to `wwwroot/Screenshots`
  (it already does, for uploads). Screenshot imports write there too.

## 5. The old ScreenshotsDev folder

There is only one screenshots folder now: `wwwroot/Screenshots`. On startup the app moves anything
left in `wwwroot/ScreenshotsDev` into it and rewrites `ScreenshotsDev/...` paths in the database to
`Screenshots/...` (also after a database import, in case the dump is older). On the VPS, which always
used `Screenshots`, this does nothing. Nothing to do by hand.

## Notes

- A database import **replaces everything** in the database, including the user accounts - after
  importing a dump from another machine you log in with the passwords stored in that dump.
- The import empties the database's `public` schema and then runs the dump, all in a single
  transaction: if it fails, the database is left exactly as it was. The database user in the
  connection string therefore needs to own the `public` schema (it does when it created/owns the
  database, the usual setup).
- A dump made with an older version of the app (e.g. before trade add-ons) can be imported: right after
  the import the app applies its database migrations, in every environment. The VPS also applies them on
  every start (Production), so deploying a version with a new migration needs nothing by hand.
- Import the same kind of dump the app exports (`.dump`, custom format). Plain `.sql` dumps made with
  `pg_dump` also work, with or without `--clean`.
- The database and the screenshots are separate files: to move everything to another server, export
  and import both.
