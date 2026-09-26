# HOWTO

Build, run, and publish instructions for Pomodoro Timer. See [README.md](README.md) for what the app does and how it's put together.

Requires the .NET 10 SDK.

## Building

```bash
dotnet build
```

Builds the whole solution: `PomodoroTimer.Core`, `PomodoroTimer.App` (both target frameworks), and the test project.

## Running locally

```bash
dotnet run --project src/PomodoroTimer.App/PomodoroTimer.App.csproj
```

Defaults to the current OS's target framework (`net10.0-windows10.0.19041.0` on Windows, `net10.0` on Linux).

## Running tests

```bash
dotnet test tests/PomodoroTimer.Core.Tests/PomodoroTimer.Core.Tests.csproj
```

## Publishing

Self-contained, single-folder builds per platform:

```bash
# Windows x64
dotnet publish src/PomodoroTimer.App/PomodoroTimer.App.csproj -c Release -r win-x64 --self-contained -f net10.0-windows10.0.19041.0 -p:PublishSingleFile=true

# Linux x64
dotnet publish src/PomodoroTimer.App/PomodoroTimer.App.csproj -c Release -r linux-x64 --self-contained -f net10.0 -p:PublishSingleFile=true
```

Output lands in `src/PomodoroTimer.App/bin/Release/<tfm>/<rid>/publish/`.
