# Pomodoro Timer

> This app is vibe coded.

A cross-platform Pomodoro / timer & stopwatch desktop app built with Avalonia UI. Runs on **Windows 11** and **Ubuntu 24.04** from a single codebase.

**Current version: 2.3.0.** See [RELEASE.md](RELEASE.md) for what changed in each release.

> **Upgrading from 2.1.x?** Session names now come from a list you keep in `names.txt`. After the first start of 2.2.1 or later, add your names to that file (one per line) and restart the app. Until then you can't start a session. See [Session names](#session-names). Your history carries over unchanged.

## Features

- **Timer** — counts down from a duration you set, with a live progress bar. Can be paused, resumed, and stopped.
- **Stopwatch** — counts up with no fixed duration. Can be paused, resumed, and stopped.
- **Custom durations** — set any hours/minutes/seconds combination for either mode. The duration boxes accept digits only; letters and symbols are ignored as you type or paste.
- **Name** — every session needs a name picked from your own list in `names.txt` (see [Session names](#session-names)). Type a letter or two and the Name box suggests matching names.
- **Mode** — every session is tagged Work, Study, or Break; a mode must be selected and a name chosen before starting. Mode and Name are cleared when a session ends, ready for the next one.
- **Time display** — always `HH:MM:SS` (e.g. `00:25:00`), whether idle, running, or paused.
- **History** — every completed or stopped session is logged with name, mode, category, duration, status, and timestamps.
  - Filters: **Type** (All / Timer / Stopwatch), **Mode** (All / Work / Study / Break), **Session** (All / Completed / Stopped early), and a **From** / **To** date range in `YYYY-MM-DD` format. Type a date or pick one from the calendar. Both days are included, and a session counts on the day it started, in local time. Clearing a date leaves that end of the range open. Filters reset to All / All / All / 7 days ago through today each time the page is opened.
  - Delete a single entry with its bin icon, or delete everything the current filters show with **Delete Filtered Data** (asks "Are you sure?" first).
  - Stopwatch sessions always count as Completed, since a stopwatch has no target to stop short of.
- **Dashboard** — totals for today and for the last 7 days, 14 days, 1 month, 3 months, 6 months, and 1 year (rolling 7/14/30/90/180/365-day windows), plus daily averages and a **Ratio** table (each category's share of the recorded time, as percentages that add up to 100) for the same periods, broken down by Work/Study/Break. Reloaded from the database every time the page is opened, and after deletes in History. It always counts all sessions and ignores History's filters.
- **Navigation** — Timer, Dashboard, and History buttons on every page; the current page is highlighted. A **Sync** button after History syncs with Google Drive on demand (see [Google Drive sync](#google-drive-sync)). A running timer or stopwatch keeps going while you view other pages.
- **Dark theme** — the app always uses a dark theme, regardless of the OS setting.
- **Closing mid-session** — if you close the app while a timer or stopwatch is running or paused, it asks first, for example "A timer is running — stop and close?". **Keep running** cancels the close. **Stop and close** saves the session exactly as if you had pressed **Stop** (a timer counts as stopped early), uploads it with the shutdown sync, and closes. When the computer is shutting down or logging off, the app saves and syncs without asking.
- **Completion notification** — when a timer reaches zero, you get a native OS toast notification and a completion sound. (Stopwatch sessions end via Stop, since counting up has no natural completion point.)
- **Google Drive sync** (optional) — keeps the same history and names list on several computers, such as a laptop and a PC. See [Google Drive sync](#google-drive-sync) below.
- **Error handling and logging** — failures are logged to daily log files next to the database and explained in plain words; the app keeps running whenever it can.

All data lives in a single local SQLite file, which is always the working copy, plus the `names.txt` names list. The only network access is the optional Google Drive sync, which uploads or downloads those whole files. There are no app accounts — sync uses your own Google account.

## Session names

The Name box only accepts names from `names.txt`, a plain text file in the app directory (next to `pomodoro.db`) with one name per line. It's here:

- Windows: `%LOCALAPPDATA%\PomodoroTimer\names.txt`
- Linux: `~/.local/share/PomodoroTimer/names.txt`

For example:

```
Deep work
Reading
Email
```

- The app creates an empty `names.txt` the first time it starts. Add or remove names in any text editor, then restart the app. Names are read once at startup, after sync.
- Typing in the Name box shows names that contain what you typed, ignoring case. Start is refused if the name isn't in the list, and the message tells you where the file is.
- Blank lines and spaces around a name are ignored. A name listed twice (in any letter case) appears once.
- Removing a name from the file doesn't change History. Past sessions keep their names.
- With sync set up, `names.txt` syncs through Google Drive the same way as the database (see below), so every computer has the same list.

## Google Drive sync

Sync is optional. Without a `client_secret.json` next to the executable the app works exactly as before and shows **Sync not configured**. [HOWTO.md](HOWTO.md#setting-up-google-drive-sync) explains the one-time Google Cloud setup.

**Use one computer at a time.** Sync assumes a single writer: close the app on one computer before using it on another. It never merges two databases or two names lists; if both sides changed, it asks you which one to keep.

How it works:

- **Sign-in** — click **Sign in** next to the sync status, or **Sync** in the top bar. Your browser opens for Google's sign-in; if it doesn't, the dialog shows a link you can open yourself. The app asks only for the `drive.file` permission, so it can see only the files it created, not the rest of your Drive.
- **Two files in Drive** — the app keeps exactly one `pomodoro.db` in your Drive, at `PomodoroTimer/pomodoro.db` (tagged with the app property `pomodoroSync=primary`), and one names list at `PomodoroTimer/names.txt` (tagged `pomodoroSync=names`). It always updates those same files. Drive keeps their earlier versions in each file's version history.
  - The app creates the `PomodoroTimer` folder itself and finds it by an app property, so renaming it is fine. A folder you create by hand isn't used, because `drive.file` doesn't let the app see it.
  - A `pomodoro.db` that an earlier version put at the top of My Drive is moved into the folder on its next upload. It keeps the same file and version history.
- **When it syncs** — at startup, before the database is opened, the window shows "Syncing…". At close, the app uploads what changed, allowing about 30 seconds; if the upload fails, the next start uploads it. There's no background sync. To sync at any other time, press **Sync** in the top bar. It runs the same checks and prompts as at startup, asks you to sign in if needed, and is disabled while a sync is running. After a problem you can also press **Retry** next to the sync status.
- **Change detection** — the app compares content hashes (MD5) of the local file, the Drive file, and the version recorded at the last sync. It doesn't compare timestamps.
  - If only Drive changed, the local copy is backed up and then replaced.
  - If only the local copy changed, it's uploaded.
  - If both changed, a dialog asks: **Keep this device's data**, **Keep cloud data**, or **Work offline for now** (nothing syncs this session and you're asked again at the next start).
  - `names.txt` follows the same rules on its own, with its own dialog (**Keep this device's names list** / **Keep cloud names list** / **Work offline for now**). The database is synced first. A choice made for one file doesn't affect the other.
- **Safety** — downloads go to a temporary file and are checked first. The database must pass SQLite's `integrity_check`, and `names.txt` must be valid UTF-8 text. Only then does the download replace the local file, in one atomic step. Before any replacement, the local file is copied to `pomodoro.db.bak-<yyyyMMdd-HHmmss>` or `names.txt.bak-<yyyyMMdd-HHmmss>` (UTC). The five newest backups of each file are kept. If the download fails the check, your local data is left alone.
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
    Models/                 Session, TimerMode, SessionCategory, CategoryTotals, CategoryRatio, DashboardReport
    Data/                   Database, SessionRepository (Microsoft.Data.Sqlite)
    Services/               TimerEngine (countdown/count-up/pause/resume/complete state machine),
                             DashboardService (totals + daily averages per category and time window)
    Names/                  NameList (reads and matches names.txt)
    Sync/                   ICloudFileStore (provider-neutral whole-file operations), SyncDecision (the startup
                             decision table as a pure function), SyncService (hashing, backups, verified downloads,
                             state files), SyncedFile (per-file checks: DatabaseSyncFile, NamesSyncFile).
                             No reference to Google libraries.
  PomodoroTimer.App/        Avalonia UI
    ViewModels/              MainWindowViewModel, DashboardViewModel, HistoryViewModel, SyncStatusViewModel and friends (plain INotifyPropertyChanged, no framework)
    Views/                   MainWindow.axaml (Timer, History, and Dashboard pages, Sync button, sync status, "Syncing…" overlay); code-behind holds the digits-only input filter.
                             MessageDialog (small modal used for the conflict prompts, sign-in link, close prompt, and error messages)
    Services/                NotificationService, SoundService, NotificationManagerFactory,
                             GoogleAuthService + GoogleDriveFileStore (Google Drive implementation of ICloudFileStore),
                             SyncCoordinator (startup, Sync button/Retry, and shutdown sync of both files, and their prompts),
                             ErrorReporter, AppPaths
    Assets/complete.wav      Bundled completion chime
tests/
  PomodoroTimer.Core.Tests/  xUnit tests for TimerEngine, DashboardService, the SQLite repositories, NameList, and sync
```

`PomodoroTimer.Core` has no reference to Avalonia — the timer/stopwatch state machine (`TimerEngine`) takes an injectable clock, so its start/pause/resume/complete transitions are tested with a fake clock rather than real `Thread.Sleep` calls.

## Data model

**Sessions** (`Sessions` table, the history/reports): `Id`, `Name`, `Mode`, `Category` (Work/Study/Break), `PlannedDurationSeconds` (null for stopwatch), `ActualDurationSeconds`, `StartedAt`, `EndedAt`, `Completed`.

The Dashboard's totals and averages are computed on the fly from this table (via `DashboardService`) rather than stored — there's no separate aggregates table to keep in sync.

The database file lives at:

- Windows: `%LOCALAPPDATA%\PomodoroTimer\pomodoro.db`
- Linux: `~/.local/share/PomodoroTimer/pomodoro.db`

The path depends only on the user account, not on where the app runs from. Debug builds (`dotnet run`, `scripts/run.*`) use a separate `PomodoroTimer-Dev` folder next to it, with their own database, names list, sync state, sign-in token, logs, and backups, so development never touches the real history. In Google Drive, a Debug build syncs `pomodoro-dev.db` (tagged `{ "pomodoroSync": "dev" }`) and `names-dev.txt` (tagged `{ "pomodoroSync": "names-dev" }`) in the same `PomodoroTimer` folder and never sees the real `pomodoro.db` or `names.txt`.

### Files in the app directory

Everything lives in the folder that contains `pomodoro.db`:

| File | Purpose | Uploaded to Drive? |
|---|---|---|
| `pomodoro.db` | The database (working copy) | **Yes** |
| `names.txt` | Session names, one per line (edited by you) | **Yes** |
| `sync-state.json` | Per-device sync bookkeeping for `pomodoro.db`: Drive file ID, hash at last sync, time of last sync | No |
| `sync-state-names.json` | The same bookkeeping for `names.txt` | No |
| `google-token/` | OAuth refresh token | No, never |
| `pomodoro-<date>.log` | Log files (14 days kept) | No |
| `pomodoro.db.bak-<timestamp>`, `names.txt.bak-<timestamp>` | Automatic backup made before the local file is replaced by a download (5 newest of each kept) | No |

No sync bookkeeping is stored inside the database. The logs never contain OAuth tokens, the client secret, or authorization codes.

## Error handling

- Failures are caught where they happen: button clicks, startup, shutdown, sync, notifications, and sound. Each one is logged with its stack trace and shown to you as a short, plain message.
- Errors nothing else caught (on the UI thread, in unobserved tasks, and anywhere else in the app) are logged too, and the app keeps running where it can.
- If the database can't be read or written (for example, the file is locked or the disk is full), you get a clear message. The app never deletes or overwrites the database in response.
- If a notification or sound fails, it's logged and the app carries on; the session is still saved.

## Known limitations

- No installer/deployment automation (per CLAUDE.md's non-goals) — publish produces a runnable folder/executable only.
- Sync is one device at a time and never merges. If two computers change the history (or the names list) before either syncs, you choose which copy to keep. The other copy stays in a local backup or in Drive's version history.
- Changes to `names.txt` show up in the app after a restart, not while it's running.

## Building, running, and publishing

See [HOWTO.md](HOWTO.md).
