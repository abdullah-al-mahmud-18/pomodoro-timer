# CLAUDE.md

## Project context

Pomodoro Timer is a cross-platform (Windows 11 + Ubuntu 24.04) timer & stopwatch desktop app built with Avalonia UI on .NET 10. It is already built and working. **README.md is the source of truth for existing features, project structure, and tech stack — read it (and HOWTO.md) before making changes.**

Key facts about the current state:

- Data lives in one local SQLite file, `pomodoro.db`, accessed via `Microsoft.Data.Sqlite` with hand-written SQL (no ORM).
  - Windows: `%LOCALAPPDATA%\PomodoroTimer\pomodoro.db`
  - Linux: `~/.local/share/PomodoroTimer/pomodoro.db`
- `PomodoroTimer.Core` is UI-agnostic and has no Avalonia reference. `PomodoroTimer.App` holds the Avalonia UI. Tests are xUnit in `tests/PomodoroTimer.Core.Tests`.
- The App multi-targets `net10.0-windows10.0.19041.0` (win-x64) and `net10.0` (linux-x64). Any new code must build for both.

## The task

Add two things:

1. **Google Drive sync** so the same history is available on multiple devices (e.g. a laptop and a PC).
2. **Proper error handling and file logging** across the whole app.

Do not change existing features, UI layout, or behavior beyond what this task requires. If something seems to need a behavior change, ask first.

### Core assumptions (do not design around anything else)

- The user uses **one device at a time**. This is a single-writer model; the app never merges two databases.
- **Data loss is the top concern.** Every design choice favors "never silently overwrite data" over convenience. When in doubt, keep a copy and ask the user.
- The local `pomodoro.db` stays the working copy. The app never reads or writes the database over the network — it only uploads/downloads the whole file.

## Before you start

- Read README.md, HOWTO.md, and the existing `Database` / `SessionRepository` code.
- Find where the database is initialized/migrated at startup — sync must run **before** that.
- Check which SQLite journal mode is used (WAL leaves data in a `-wal` side file; see "Shutdown sync").
- Check how the app currently handles closing while a timer/stopwatch is running. Keep that behavior; just make sure whatever gets saved is uploaded.

## New dependencies

- `Google.Apis.Drive.v3` and `Google.Apis.Auth` (official Google libraries) — App project only.
- `Serilog` + `Serilog.Sinks.File` — for logging.
- Nothing else without asking.

## App directory files

All files live in the existing per-user app directory (the folder containing `pomodoro.db`):

| File                          | Purpose                                     | Uploaded to Drive?                    |
| ----------------------------- | ------------------------------------------- | ------------------------------------- |
| `pomodoro.db`                 | Database (working copy)                     | **Yes — the only file ever uploaded** |
| `sync-state.json`             | Per-device sync bookkeeping                 | No                                    |
| `google-token/`               | OAuth refresh token                         | No — never                            |
| `pomodoro-<date>.log`         | Log files                                   | No                                    |
| `pomodoro.db.bak-<timestamp>` | Automatic backup before any local overwrite | No                                    |

No sync bookkeeping goes inside the database.

## Google Drive sync

### Authentication

- OAuth 2.0 installed-app flow via `GoogleWebAuthorizationBroker.AuthorizeAsync` (system browser + localhost loopback redirect). The user signs in with their own Google account.
- Scope: **`https://www.googleapis.com/auth/drive.file` only.** Do not request any broader scope.
- Token storage: `FileDataStore` with the full-path option, pointed at `<app dir>/google-token`.
- Client credentials: load from `client_secret.json` next to the executable (`AppContext.BaseDirectory`). **Never commit this file** — add it to `.gitignore`. If it's missing, the app runs normally with sync disabled and shows "Sync not configured".
- Verify the browser actually opens on Ubuntu. If launching the browser fails, show the sign-in URL so the user can open it manually.
- Revoked/expired refresh token (`TokenResponseException`): delete the stored token, show "Needs sign-in", let the user sign in again. Never crash.

### The Drive file

- Exactly one file named `pomodoro.db`, tagged with `appProperties` `{ "pomodoroSync": "primary" }`.
- Store its Drive file ID in `sync-state.json`. After the first upload, always **update** that same file ID — never create a second file. (Drive keeps revision history, which is a recovery safety net.)
- On a device with no stored file ID, find the file with the query `appProperties has { key='pomodoroSync' and value='primary' } and trashed = false`. If more than one match comes back, don't guess — log it and ask the user.

### Change detection — content hashes, not timestamps

Compare three MD5 values (MD5 is used only for change detection, not security):

- `L` = MD5 of the local `pomodoro.db` right now
- `C` = the Drive file's `md5Checksum` (a metadata request — no download needed)
- `S` = the MD5 recorded in `sync-state.json` at the last successful sync (upload or download)

`sync-state.json` holds: Drive file ID, `S`, and the UTC time of the last successful sync. It is per-device and never uploaded. Write it atomically (temp file + replace).

### Startup sync

Runs **before** the database is opened or migrated. Show the main window in a "Syncing…" state while it runs; load pages only after it finishes.

| Situation                                                                    | Action                                                                                                                  |
| ---------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| No cloud file exists                                                         | Upload the local db (create the file). Record file ID and `S`.                                                          |
| Cloud file exists, no `sync-state.json`, local db missing or has no sessions | Download.                                                                                                               |
| Cloud file exists, no `sync-state.json`, local db has sessions               | **Conflict** — ask the user.                                                                                            |
| `L == S` and `C == S`                                                        | In sync. Do nothing.                                                                                                    |
| `L == S` and `C != S`                                                        | Cloud is newer → back up local, then download.                                                                          |
| `L != S` and `C == S`                                                        | Local changes never reached the cloud (last close crashed or was offline) → upload, then continue. Show a brief notice. |
| `L != S` and `C != S`                                                        | **Conflict** — ask the user.                                                                                            |

**Conflict dialog** (Avalonia has no built-in message box — create a small modal window) with three choices:

- **Keep this device's data** → upload. The cloud's previous version remains in Drive's revision history.
- **Keep cloud data** → back up local, then download.
- **Work offline for now** → open the local db, sync nothing this session, ask again at next start. Shutdown upload is skipped in this state.

Never resolve a conflict automatically.

### Shutdown sync

On window close: cancel the close, show "Syncing…", run the steps below, then close for real.

1. Make sure all data is saved. Close every SQLite connection and call `SqliteConnection.ClearAllPools()`. Make sure the `.db` file is complete on its own — no pending `-wal`/`-journal` content (switch to `journal_mode=DELETE`, or run `PRAGMA wal_checkpoint(TRUNCATE)` before closing).
2. Compute `L`. If `L == S`, nothing changed — skip the upload.
3. Fetch `C`. If `C != S`, **do not upload** — another device changed the cloud copy. Log it; the next startup will show the conflict dialog.
4. Otherwise upload, then record the new `S`.

- Use a timeout of about 30 seconds. If the upload fails or times out, close anyway and log it. The next startup's `L != S, C == S` rule uploads the pending changes, so nothing is lost.
- Guard against the close handler running twice.

### Downloading and replacing the local db

- Download to a temp file in the app dir. Verify it opens and `PRAGMA integrity_check` returns `ok`. Then replace `pomodoro.db` atomically. Never download directly over the live file.
- If verification fails: keep the local db untouched, log it, and tell the user.
- Before replacing the local db for any reason, copy it to `pomodoro.db.bak-<yyyyMMdd-HHmmss>`. Keep the 5 most recent backups.

### Offline and failure behavior

- Sync failures never block local use. The app always opens the local db and works normally.
- Show a small, unobtrusive sync status (e.g. "Synced 10:42", "Offline — will sync later", "Sync not configured", "Needs sign-in") with a way to retry.
- Use the Google client's built-in exponential backoff for transient failures (5xx, rate limits, network errors).

### Where the code goes

- **Core** (`PomodoroTimer.Core/Sync/`), with no reference to `Google.Apis.*`:
  - `ICloudFileStore` — find file, get metadata (ID + MD5), create, update, download.
  - `SyncDecision` — a pure function that takes `L`, `C`, `S` plus "cloud exists / state exists / local has sessions" flags and returns the action from the startup table.
  - `SyncService` — orchestration: hashing, backups, temp-file download + verification, state-file updates. Depends on `ICloudFileStore`, not on Google directly.
- **App** (`PomodoroTimer.App/Services/`):
  - `GoogleDriveFileStore : ICloudFileStore` plus the auth code.
  - Conflict dialog, sync status UI, and startup/shutdown wiring.

## Error handling and logging

### Logging

- Serilog, rolling daily file sink, written to **the same directory as `pomodoro.db`**: `pomodoro-<date>.log`. Keep 14 files; cap file size (e.g. 10 MB).
- Configure logging first thing in `Main`, so startup failures are captured. Call `Log.CloseAndFlush()` on exit.
- Log every caught exception with its stack trace, all unhandled exceptions, and sync events at Information level (decision taken, upload/download result, conflicts, backups created).
- **Never log** OAuth tokens, the client secret, or authorization codes.
- A logging failure must never crash the app.

### Exception handling

- Global handlers that log, and keep the app running where possible: `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`, and Avalonia's `Dispatcher.UIThread.UnhandledException`.
- Catch at boundaries — UI command handlers, startup, shutdown, sync, notification and sound calls — not deep inside Core logic. Every `catch` logs; nothing is swallowed silently.
- Specific cases:
  - `SqliteException` / `IOException` (locked file, disk full, missing file) → show a clear message. Never delete or overwrite the db in response.
  - `HttpRequestException`, timeouts, `TaskCanceledException` → treat as offline.
  - `GoogleApiException`: 401 → re-authenticate; 403/429 → back off and retry; 404 → the cloud file was deleted, so ask the user before re-uploading.
  - `TokenResponseException` → re-authenticate (see Authentication).
  - Notification or sound failures → log and continue. The session is still saved.
- User-facing messages are short and plain, e.g. "Couldn't reach Google Drive — your data is saved on this device and will sync later." No stack traces in the UI.
- `async void` only for event handlers, and each one wraps its body in try/catch.

## Tests

Add xUnit tests in `PomodoroTimer.Core.Tests`:

- Every row of the startup decision table.
- `SyncService` with a fake `ICloudFileStore`: upload/download paths, skip-upload when `C != S` at shutdown, failed download verification leaving the local db untouched, backup creation and rotation (keep 5).
- Existing tests must still pass.

## Documentation

- **README.md**: replace "no cloud sync, no accounts, no network access" with a description of the sync feature; add the new packages to the tech stack table; list the new files in the app directory; note the one-device-at-a-time assumption.
- **HOWTO.md**: how to set up the Google Cloud project — enable the Drive API, configure the OAuth consent screen (External, `drive.file` scope only, publish to "In production" so refresh tokens don't expire after 7 days), create a "Desktop app" OAuth client, and place `client_secret.json` next to the executable (dev and published builds).
- **.gitignore**: add `client_secret.json`.

## Non-goals

- No cloud provider other than Google Drive, no custom server, no app-level user accounts.
- No merging of divergent databases.
- No background or periodic sync — only at startup and shutdown (plus manual retry).
- No installer or deployment automation.

## Definition of done

- `win-x64` and `linux-x64` both build and publish.
- All tests pass.
- Manually verified on both OSes: first sign-in, upload from device A, download on device B, conflict dialog, offline start, failed shutdown upload recovered on next start, and log files appearing next to `pomodoro.db`.
