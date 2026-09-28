# Pomodoro Timer

> This app is vibe coded.

A cross-platform Pomodoro / timer & stopwatch desktop app built with Avalonia UI. Runs on **Windows 11** and **Ubuntu 24.04** from a single codebase.

## Features

- **Timer** — counts down from a duration you set, with a live progress bar. Can be paused, resumed, and stopped.
- **Stopwatch** — counts up with no fixed duration. Can be paused, resumed, and stopped.
- **Custom durations** — set any hours/minutes/seconds combination, with a name, for either mode. The duration boxes accept digits only; letters and symbols are ignored as you type or paste.
- **Mode** — every session is tagged Work, Study, or Break; a mode must be selected before starting. Mode and Name are cleared when a session ends, ready for the next one.
- **Time display** — always `HH:MM:SS` (e.g. `00:25:00`), whether idle, running, or paused.
- **History** — every completed or stopped session is logged with name, mode, category, duration, status, and timestamps.
  - Filters: **Type** (All / Timer / Stopwatch), **Mode** (All / Work / Study / Break), **Session** (All / Completed / Stopped early), and **Period** (All / last 7 / 14 / 30 / 180 / 365 days). Filters reset to All / All / All / Last 7 days each time the page is opened.
  - Delete a single entry with its bin icon, or delete everything the current filters show with **Delete Filtered Data** (asks "Are you sure?" first).
  - Stopwatch sessions always count as Completed, since a stopwatch has no target to stop short of.
- **Dashboard** — totals for today and rolling 7/14/30/90/365-day windows, plus daily averages for the 7/14/30/90-day windows, broken down by Work/Study/Break. Reloaded from the database every time the page is opened, and after deletes in History. It always counts all sessions and ignores History's filters.
- **Navigation** — Timer, Dashboard, and History buttons on every page; the current page is highlighted. A running timer or stopwatch keeps going while you view other pages.
- **Dark theme** — the app always uses a dark theme, regardless of the OS setting.
- **Completion notification** — when a timer reaches zero, you get a native OS toast notification and a completion sound. (Stopwatch sessions end via Stop, since counting up has no natural completion point.)

- **Google Drive sync** (optional) — keeps the same history on several computers, such as a laptop and a PC. See [Google Drive sync](#google-drive-sync) below.
- **Error handling and logging** — failures are logged to daily log files next to the database and explained in plain words; the app keeps running whenever it can.

All data lives in a single local SQLite file, which is always the working copy. The only network access is the optional Google Drive sync, which uploads or downloads that whole file. There are no app accounts — sync uses your own Google account.

## Google Drive sync

Sync is optional. Without a `client_secret.json` next to the executable the app works exactly as before and shows **Sync not configured**. [HOWTO.md](HOWTO.md#setting-up-google-drive-sync) explains the one-time Google Cloud setup.

**Use one computer at a time.** Sync assumes a single writer: close the app on one computer before using it on another. It never merges two databases; if both sides changed, it asks you which one to keep.

How it works:

- **Sign-in** — click **Sign in** in the title bar. Your browser opens for Google's sign-in; if it doesn't, the dialog shows a link you can open yourself. The app asks only for the `drive.file` permission, so it can see only the file it created, not the rest of your Drive.
- **One file in Drive** — the app keeps exactly one `pomodoro.db` in your Drive, at `PomodoroTimer/pomodoro.db` (tagged with the app property `pomodoroSync=primary`), and always updates that same file. Drive keeps its earlier versions in the file's version history.
  - The app creates the `PomodoroTimer` folder itself and finds it by an app property, so renaming it is fine. A folder you create by hand isn't used, because `drive.file` doesn't let the app see it.
  - A `pomodoro.db` that an earlier version put at the top of My Drive is moved into the folder on its next upload. It keeps the same file and version history.
- **When it syncs** — at startup, before the database is opened, the window shows "Syncing…". At close, the app uploads what changed, allowing about 30 seconds; if the upload fails, the next start uploads it. There's no background sync; after a problem you can press **Retry** in the title bar.
- **Change detection** — the app compares content hashes (MD5) of the local file, the Drive file, and the version recorded at the last sync. It doesn't compare timestamps.
  - If only Drive changed, the local copy is backed up and then replaced.
  - If only the local copy changed, it's uploaded.
  - If both changed, a dialog asks: **Keep this device's data**, **Keep cloud data**, or **Work offline for now** (nothing syncs this session and you're asked again at the next start).
- **Safety** — downloads go to a temporary file, are checked with SQLite's `integrity_check`, and only then replace the local file in one atomic step. Before any replacement, the local file is copied to `pomodoro.db.bak-<yyyyMMdd-HHmmss>` (UTC). The five newest backups are kept. If the download fails the check, your local data is left alone.
- **Offline** — if Google Drive can't be reached, the app opens your local data as normal and shows "Offline — will sync later".
- **Status** — the title bar shows the sync status, for example "Synced 10:42", "Offline — will sync later", "Sync not configured", or "Needs sign-in". Hover over it for details.

## Tech stack

| Concern | Choice |
|---|---|
| UI framework | [Avalonia UI](https://avaloniaui.net/) (XAML, Fluent theme, dark variant only) |
| Runtime | .NET 10 |
| Storage | SQLite via `Microsoft.Data.Sqlite` (no ORM, hand-written SQL) |
| Notifications | `DesktopNotifications` + `DesktopNotifications.Windows` / `DesktopNotifications.FreeDesktop` |
| Audio | `NetCoreAudio` (cross-platform; avoids Windows-only libraries like NAudio) |
| Cloud sync | `Google.Apis.Drive.v3` + `Google.Apis.Auth` (official Google client libraries; App project only) |
| Logging | `Serilog` + `Serilog.Sinks.File` (daily rolling files, 14 kept, 10 MB cap per file) |

### A note on notifications

CLAUDE.md specifies `DesktopNotifications.Avalonia` for wiring notifications into the Avalonia `AppBuilder`. That package (1.3.1) is compiled against a pre-1.0 Avalonia API (`AppBuilderBase<T>`, an old `AvaloniaLocator.Current`) and does not compile against Avalonia 11. Instead, [`NotificationManagerFactory`](src/PomodoroTimer.App/Services/NotificationManagerFactory.cs) constructs the platform-specific manager directly from `DesktopNotifications.Windows` / `DesktopNotifications.FreeDesktop` — the same underlying backends, just without the incompatible Avalonia glue.

This also means the app multi-targets two TFMs:

- `net10.0-windows10.0.19041.0` — used for the `win-x64` build. `DesktopNotifications.Windows`'s toast implementation (`Microsoft.Toolkit.Uwp.Notifications`) needs a Windows-flavored TFM to resolve to a WinRT-capable build; on a plain `net10.0` TFM it resolves to a stub that throws at runtime.
- `net10.0` (no suffix) — used for the `linux-x64` build, where only the FreeDesktop/D-Bus notification path is compiled in.

## Project structure

```
PomodoroTimer.slnx
src/
  PomodoroTimer.Core/       UI-agnostic domain logic (testable in isolation)
    Models/                 Session, TimerMode, SessionCategory, CategoryTotals, DashboardReport
    Data/                   Database, SessionRepository (Microsoft.Data.Sqlite)
    Services/               TimerEngine (countdown/count-up/pause/resume/complete state machine),
                             DashboardService (totals + daily averages per category and time window)
    Sync/                   ICloudFileStore (provider-neutral whole-file operations), SyncDecision (the startup
                             decision table as a pure function), SyncService (hashing, backups, verified downloads,
                             sync-state.json). No reference to Google libraries.
  PomodoroTimer.App/        Avalonia UI
    ViewModels/              MainWindowViewModel, DashboardViewModel, HistoryViewModel, SyncStatusViewModel and friends (plain INotifyPropertyChanged, no framework)
    Views/                   MainWindow.axaml (Timer, History, and Dashboard pages, sync status, "Syncing…" overlay); code-behind holds the digits-only input filter.
                             MessageDialog (small modal used for the conflict prompt, sign-in link, and error messages)
    Services/                NotificationService, SoundService, NotificationManagerFactory,
                             GoogleAuthService + GoogleDriveFileStore (Google Drive implementation of ICloudFileStore),
                             SyncCoordinator (startup/retry/shutdown sync and prompts), ErrorReporter, AppPaths
    Assets/complete.wav      Bundled completion chime
tests/
  PomodoroTimer.Core.Tests/  xUnit tests for TimerEngine, DashboardService, and the SQLite repositories
```

`PomodoroTimer.Core` has no reference to Avalonia — the timer/stopwatch state machine (`TimerEngine`) takes an injectable clock, so its start/pause/resume/complete transitions are tested with a fake clock rather than real `Thread.Sleep` calls.

## Data model

**Sessions** (`Sessions` table, the history/reports): `Id`, `Name`, `Mode`, `Category` (Work/Study/Break), `PlannedDurationSeconds` (null for stopwatch), `ActualDurationSeconds`, `StartedAt`, `EndedAt`, `Completed`.

The Dashboard's totals and averages are computed on the fly from this table (via `DashboardService`) rather than stored — there's no separate aggregates table to keep in sync.

The database file lives at:

- Windows: `%LOCALAPPDATA%\PomodoroTimer\pomodoro.db`
- Linux: `~/.local/share/PomodoroTimer/pomodoro.db`

The path depends only on the user account, not on where the app runs from, so a local development build and a published build on the same machine share the same history.

### Files in the app directory

Everything lives in the folder that contains `pomodoro.db`:

| File | Purpose | Uploaded to Drive? |
|---|---|---|
| `pomodoro.db` | The database (working copy) | **Yes, and it's the only file ever uploaded** |
| `sync-state.json` | Per-device sync bookkeeping: Drive file ID, hash at last sync, time of last sync | No |
| `google-token/` | OAuth refresh token | No, never |
| `pomodoro-<date>.log` | Log files (14 days kept) | No |
| `pomodoro.db.bak-<timestamp>` | Automatic backup made before the local database is replaced (5 newest kept) | No |

No sync bookkeeping is stored inside the database. The logs never contain OAuth tokens, the client secret, or authorization codes.

## Error handling

- Failures are caught where they happen: button clicks, startup, shutdown, sync, notifications, and sound. Each one is logged with its stack trace and shown to you as a short, plain message.
- Errors nothing else caught (on the UI thread, in unobserved tasks, and anywhere else in the app) are logged too, and the app keeps running where it can.
- If the database can't be read or written (for example, the file is locked or the disk is full), you get a clear message. The app never deletes or overwrites the database in response.
- If a notification or sound fails, it's logged and the app carries on; the session is still saved.

## Known limitations

- No installer/deployment automation (per CLAUDE.md's non-goals) — publish produces a runnable folder/executable only.
- Sync is one device at a time and never merges. If two computers change the history before either syncs, you choose which copy to keep. The other copy stays in a local backup or in Drive's version history.
- Closing the app while a timer or stopwatch is running doesn't save that session, as before. Everything that was saved is uploaded at close.

## Building, running, and publishing

See [HOWTO.md](HOWTO.md).
