# StoreAPI

## Web dashboard (Blazor WebAssembly)

The **`StoreWeb`** project is referenced here so the full WASM static asset graph (including compressed `.wasm` / assemblies) is served via **`MapStaticAssets()`**. Copying only `StoreWeb\bin\...\wwwroot` into `StoreAPI\wwwroot` is **not** enough on modern .NET and leaves the browser stuck on the loading spinner.

### Same folder as Store POS (portable publish)

MAUI **BlazorWebView** must keep **`wwwroot/`** for `StorePOS.styles.css` and **`blazor.webview.js`**. The merged **`publish-windows.ps1`** layout therefore puts the browser SPA in **`browserwww/`** next to the exes. **StoreAPI** calls **`UseWebRoot("browserwww")`** when that folder exists; otherwise it uses the normal **`wwwroot/`** (API-only deploy).

## SQLite backups to Discord (Phase 9.5)

The API runs a background job that copies the SQLite database (using SQLite’s online backup API), zips it, and posts the zip to a Discord webhook. Each run appends one line to `logs/backup.log` under the API content root (success, skip, or failure).

### One-time Discord setup

1. Open Discord → your server.
2. Create or open a channel named exactly **`kaif-database-backup`**.
3. Channel settings (gear) → **Integrations** → **Webhooks** → **New Webhook** → copy **Webhook URL**.
4. Configure the URL in either place:
   - **`BackupSettings:DiscordWebhookUrl`** in configuration (see `appsettings.json` — leave empty in source control), or
   - **Store POS** → **Settings** (admin) → **Database backup** section (saved in the database).

Prefer **user secrets** or environment variables for the webhook in development so the real URL is never committed.

### Configuration (`appsettings.json`)

```json
"BackupSettings": {
  "DiscordWebhookUrl": "",
  "IntervalHours": 24,
  "DatabasePath": "store.db",
  "BackupTempFolder": "backups"
}
```

The live database path is taken from **`ConnectionStrings:DefaultConnection`** (same as EF Core). `DatabasePath` is documentary; `BackupTempFolder` is relative to the API content root.

### Limits and behavior

- Discord webhook attachments are limited to **25 MB**. If the zip grows beyond that, the job logs a failure and skips the upload until you change strategy (e.g. external storage + link only).
- The job runs **once when the API starts**, then every **N** hours (from the database interval if set to 12/24/48, otherwise `IntervalHours` in config, defaulting to 24).
- If the webhook is missing or still set to a placeholder, the job **skips** upload and logs a warning; the API keeps running.

### Admin HTTP API

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/settings/backup` | Webhook URL, interval, last backup time (admin JWT). |
| `PUT` | `/api/settings/backup` | Body: `discordWebhookUrl`, `backupIntervalHours` (12, 24, or 48). |
| `POST` | `/api/backup/run-now` | Run one backup cycle immediately. |
| `GET` | `/api/backup/last-run` | `{ "lastBackupUtc": "..." }` |
