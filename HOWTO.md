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

Close the app before rebuilding. On Windows a running copy locks its output files, and `dotnet build` fails with `MSB3027: Could not copy ... The file is locked by: "PomodoroTimer.App"`.

A local run uses the same database as any published build on the machine (see [Data model](README.md#data-model)), so sessions you record while testing show up in the published app too. To start from an empty history, close the app and delete `pomodoro.db`; it's recreated on the next launch. If sync is set up and a copy exists in Google Drive, the next launch downloads it again. To start completely fresh, also delete `sync-state.json` and the `PomodoroTimer` folder (which holds `pomodoro.db`) in Google Drive.

Log files (`pomodoro-<date>.log`) are written to the same folder as `pomodoro.db`.

## Setting up Google Drive sync

Sync is optional. Without a `client_secret.json` the app runs normally and shows **Sync not configured**. You do this setup once. Every computer then uses the same `client_secret.json`, and each person signs in with their own Google account.

### 1. Create a Google Cloud project and enable the Drive API

1. Open the [Google Cloud console](https://console.cloud.google.com/) and create a project, for example "Pomodoro Timer".
2. Go to **APIs & Services → Library**, search for **Google Drive API**, and click **Enable**.

### 2. Configure the OAuth consent screen

1. Go to **APIs & Services → OAuth consent screen**. In newer consoles this is **Google Auth Platform → Branding / Audience / Data access**.
2. **User type / Audience:** choose **External**.
3. Enter an app name (for example "Pomodoro Timer"), a user support email, and a developer contact email.
4. **Scopes / Data access:** add only `https://www.googleapis.com/auth/drive.file` ("See, edit, create, and delete only the specific Google Drive files you use with this app"). Don't add any broader Drive scope. `drive.file` is a non-sensitive scope, so Google doesn't require verification for it.
5. **Publishing status:** click **Publish app** to move it from *Testing* to **In production**. In *Testing* mode, Google expires refresh tokens after 7 days and you'd have to sign in again every week. Unverified apps show a "Google hasn't verified this app" warning at sign-in; click **Advanced → Go to Pomodoro Timer** to continue.

### 3. Create a Desktop OAuth client

1. Go to **APIs & Services → Credentials → Create credentials → OAuth client ID**. In newer consoles this is **Google Auth Platform → Clients → Create client**.
2. **Application type:** **Desktop app**. Give it any name.
3. Click **Download JSON** and rename the file to `client_secret.json`.

### 4. Put `client_secret.json` next to the executable

The app looks for `client_secret.json` in the folder that contains the executable (`AppContext.BaseDirectory`).

- **Development (`dotnet run` / `scripts/run.*`):** put it in `src/PomodoroTimer.App/`. The build copies it into the output folder.
- **Published builds:** if it's in `src/PomodoroTimer.App/` when you publish, it's copied into `publish/<runtime>/` next to the executable. It's kept as a separate file, not bundled into the single file. For a build you already published, copy it into the same folder as `PomodoroTimer.App.exe` / `PomodoroTimer.App`.

`client_secret.json` is in `.gitignore`. **Never commit it.**

### 5. Sign in

Start the app and click **Sign in** in the title bar. Your browser opens so you can sign in to Google. On Ubuntu this uses `xdg-open`. If no browser opens, copy the link shown in the sign-in dialog into a browser. After you allow access, the app syncs straight away. The database goes to `PomodoroTimer/pomodoro.db` in your My Drive, and the app creates the folder itself. After that it syncs at every start and close.

To sign out on a computer, close the app and delete the `google-token` folder in the app directory.

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
