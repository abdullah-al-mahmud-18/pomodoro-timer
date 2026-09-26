# Pomodoro Timer

> This app is vibe coded.

A cross-platform Pomodoro / timer & stopwatch desktop app built with Avalonia UI. Runs on **Windows 11** and **Ubuntu 24.04** from a single codebase.

## Features

- **Timer** — counts down from a duration you set, with a live progress bar.
- **Stopwatch** — counts up with no fixed duration.
- **Custom durations** — set any hours/minutes/seconds combination, with a name, for either mode.
- **Presets** — save a named timer/stopwatch configuration and reapply it later (e.g. "Deep work – 50 min", "Short break – 5 min").
- **History** — every completed or stopped session is logged with name, mode, duration, and timestamps; entries can be deleted individually.
- **Completion notification** — when a timer reaches zero, you get a native OS toast notification and a completion sound. (Stopwatch sessions end via Stop, since counting up has no natural completion point.)

All data is stored locally in a single SQLite file — no cloud sync, no accounts, no network access.

## Tech stack

| Concern | Choice |
|---|---|
| UI framework | [Avalonia UI](https://avaloniaui.net/) (XAML, Fluent theme) |
| Runtime | .NET 10 |
| Storage | SQLite via `Microsoft.Data.Sqlite` (no ORM, hand-written SQL) |
| Notifications | `DesktopNotifications` + `DesktopNotifications.Windows` / `DesktopNotifications.FreeDesktop` |
| Audio | `NetCoreAudio` (cross-platform; avoids Windows-only libraries like NAudio) |

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
    Models/                 Preset, Session, TimerMode
    Data/                   Database, PresetRepository, SessionRepository (Microsoft.Data.Sqlite)
    Services/               TimerEngine — the countdown/count-up/pause/resume/complete state machine
  PomodoroTimer.App/        Avalonia UI
    ViewModels/              MainWindowViewModel and friends (plain INotifyPropertyChanged, no framework)
    Views/                   MainWindow.axaml
    Services/                NotificationService, SoundService, NotificationManagerFactory
    Assets/complete.wav      Bundled completion chime
tests/
  PomodoroTimer.Core.Tests/  xUnit tests for TimerEngine and the SQLite repositories
```

`PomodoroTimer.Core` has no reference to Avalonia — the timer/stopwatch state machine (`TimerEngine`) takes an injectable clock, so its start/pause/resume/complete transitions are tested with a fake clock rather than real `Thread.Sleep` calls.

## Data model

**Presets** (`Presets` table): `Id`, `Name`, `Mode` (`Timer`/`Stopwatch`), `DurationSeconds` (null for stopwatch), `CreatedAt`.

**Sessions** (`Sessions` table, the history/reports): `Id`, `Name`, `Mode`, `PlannedDurationSeconds` (null for stopwatch), `ActualDurationSeconds`, `StartedAt`, `EndedAt`, `Completed`.

The database file lives at:

- Windows: `%LOCALAPPDATA%\PomodoroTimer\pomodoro.db`
- Linux: `~/.local/share/PomodoroTimer/pomodoro.db`

## Known limitations

- No installer/deployment automation (per CLAUDE.md's non-goals) — publish produces a runnable folder/executable only.
- The `Tmds.DBus` / `Tmds.DBus.Protocol` packages pulled in transitively by the Linux notification backend (`DesktopNotifications.FreeDesktop`) carry an unfixed NuGet security advisory as of writing (no patched version is published upstream). This only affects the Linux D-Bus notification code path.

## Building, running, and publishing

See [HOWTO.md](HOWTO.md).
