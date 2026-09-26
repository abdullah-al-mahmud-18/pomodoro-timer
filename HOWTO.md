# HOWTO

Build, run, and publish instructions for Pomodoro Timer. See [README.md](README.md) for what the app does and how it's put together.

Requires the .NET 10 SDK.

## Building

```bash
dotnet build
```

Builds the whole solution: `PomodoroTimer.Core`, `PomodoroTimer.App` (both target frameworks), and the test project.

## Scripts

The `scripts/` folder wraps the run and publish commands below, with a bash and a PowerShell version of each. Every script picks the correct target framework for the current OS.

| Task | Bash (Ubuntu, or Git Bash on Windows) | PowerShell (Windows, or `pwsh` on Linux) |
|---|---|---|
| Run locally | `./scripts/run.sh` | `.\scripts\run.ps1` |
| Publish for the current OS | `./scripts/publish.sh` | `.\scripts\publish.ps1` |
| Publish for a specific target | `./scripts/publish.sh win-x64` / `linux-x64` / `all` | `.\scripts\publish.ps1 -Runtime win-x64` / `linux-x64` / `all` |

Published builds go to `publish/<runtime>/`.

On Ubuntu, make the bash scripts executable once with `chmod +x scripts/*.sh`, or run them as `bash scripts/run.sh`. If PowerShell blocks the `.ps1` scripts, run them with `pwsh -ExecutionPolicy Bypass -File scripts\run.ps1`.

## Running locally

The project targets two frameworks, so `dotnet run` needs `-f` to choose one:

```bash
# Windows
dotnet run --project src/PomodoroTimer.App/PomodoroTimer.App.csproj -f net10.0-windows10.0.19041.0

# Linux
dotnet run --project src/PomodoroTimer.App/PomodoroTimer.App.csproj -f net10.0
```

## Running tests

```bash
dotnet test tests/PomodoroTimer.Core.Tests/PomodoroTimer.Core.Tests.csproj
```

## Publishing

Self-contained, single-folder builds per platform. The .NET runtime is bundled, so the target machine doesn't need .NET installed. Either platform can be published from Windows or Linux.

```bash
# Windows x64
dotnet publish src/PomodoroTimer.App/PomodoroTimer.App.csproj -c Release -r win-x64 --self-contained -f net10.0-windows10.0.19041.0 -p:PublishSingleFile=true -o publish/win-x64

# Linux x64
dotnet publish src/PomodoroTimer.App/PomodoroTimer.App.csproj -c Release -r linux-x64 --self-contained -f net10.0 -p:PublishSingleFile=true -o publish/linux-x64
```

Each output folder contains the app executable (`PomodoroTimer.App.exe` on Windows, `PomodoroTimer.App` on Linux) next to a few native libraries (SkiaSharp, HarfBuzz, SQLite). Copy the whole folder when distributing.
