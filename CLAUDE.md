# CLAUDE.md

## Project
A cross-platform Pomodoro / timer & stopwatch desktop app. Must run on **Windows 11** and **Ubuntu 24.04** from a single codebase. Scope is intentionally minimal — do not add features beyond what's listed below without confirming first.

## Tech Stack
- **UI Framework:** Avalonia UI (XAML-based .NET cross-platform desktop framework). Do NOT use WPF, .NET MAUI, or any web-view-based approach (Blazor Hybrid, Photino, Electron) — these were considered and explicitly rejected.
- **Language/Runtime:** .NET 8 (or latest LTS).
- **Storage:** SQLite, a single local file (e.g. `pomodoro.db`), accessed directly via `Microsoft.Data.Sqlite` — no ORM (no EF Core). No cloud storage, no external server, no network calls of any kind.
- **Notifications:** `DesktopNotifications` + `DesktopNotifications.Avalonia` NuGet packages for native OS toast notifications — Windows native notification system, Linux via FreeDesktop/D-Bus. Requires app registration via `ApplicationContext` per platform.
- **Audio:** A genuinely cross-platform playback library — `Aural` (OpenAL-based) or `NetCoreAudio`. Do **NOT** use `NAudio` — despite being a .NET library, it is Windows-only.

## Core Features
1. **Stopwatch** — start / pause / stop; counts up with no fixed duration.
2. **Timer** — counts down from a set duration.
3. **Custom durations** — user can set an arbitrary duration with a name, for either stopwatch or timer, not just from presets.
4. **Presets** — named, reusable timer/stopwatch configurations the user can save and reuse (e.g. "Deep work – 50 min", "Short break – 5 min").
5. **History / reports** — every completed or stopped session is logged: name, mode, duration, start/end timestamp.
6. **Delete reports** — user can delete individual history entries.
7. **Completion notification** — when a timer reaches zero: show a native OS notification AND play a sound. (Stopwatch has no natural "completion" event — this applies to timers only, unless the user requests otherwise.)

## Explicit Non-Goals
- No cloud sync, no user accounts, no network access.
- No mobile targets — Windows 11 and Ubuntu 24.04 desktop only.
- No installer/deployment automation unless requested later.

## Suggested Data Model
- **Presets**: `id`, `name`, `mode` (timer/stopwatch), `duration_seconds` (nullable for stopwatch), `created_at`
- **Sessions** (history/reports): `id`, `name`, `mode`, `planned_duration_seconds` (nullable), `actual_duration_seconds`, `started_at`, `ended_at`, `completed` (bool)

## Distribution
- Publish self-contained per RID: `win-x64` and `linux-x64` via `dotnet publish`.
- Single executable/folder per platform is sufficient; no installer needed unless asked.

## Implementation Notes
- Avalonia on Linux renders via Skia over X11/Wayland — there is no WebKitGTK or browser dependency (that concern only applies to the rejected web-view approaches).
- Keep timer/stopwatch logic in a UI-agnostic class/service, independent of Avalonia views, so core logic is testable in isolation.
