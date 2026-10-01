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
| Add the launcher entry and icon on Ubuntu (see [below](#showing-the-app-icon-on-ubuntu)) | `./scripts/install-desktop-entry.sh` | — |

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

A local run is a Debug build, and it keeps its data apart from the published app (see [Data model](README.md#data-model)):

- Local files go to a separate dev folder: `%LOCALAPPDATA%\PomodoroTimer-Dev\` on Windows or `~/.local/share/PomodoroTimer-Dev/` on Linux. That folder holds the dev `pomodoro.db`, `sync-state.json`, `google-token/`, logs, and backups.
- Sync uses `pomodoro-dev.db` in the same `PomodoroTimer` folder in Google Drive. The real `pomodoro.db` there is never read or written by a dev run.

The first dev run asks you to sign in to Google once (the dev token is stored separately). To start from an empty dev history, close the app and delete `pomodoro.db` in the dev folder. If sync is set up and `pomodoro-dev.db` exists in Google Drive, the next launch downloads it again. To start completely fresh, also delete the dev folder's `sync-state.json` and `pomodoro-dev.db` in Google Drive. Leave the real `pomodoro.db` there alone.

Log files (`pomodoro-<date>.log`) are written to the same folder as the database in use. The first log line of each run shows whether it's a development or release build and which folder it uses.

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

### Showing the app icon on Ubuntu

GNOME (Ubuntu's desktop) doesn't use the icon a window sets for itself, so the dock, Alt-Tab, and Activities show a generic icon until the app has a launcher entry. After publishing, run once:

```bash
./scripts/install-desktop-entry.sh                        # uses publish/linux-x64/PomodoroTimer.App
./scripts/install-desktop-entry.sh /path/to/PomodoroTimer.App   # or a folder you copied elsewhere
```

This writes `~/.local/share/applications/pomodoro-timer.desktop` and copies the icon to `~/.local/share/icons/`. "Pomodoro Timer" then appears in the app grid, where you can pin it to the dock. Windows started with `./scripts/run.sh` get the icon too. Run the script again if you move the published folder, and remove the entry with `./scripts/install-desktop-entry.sh --uninstall`.
