# Release notes

## 2.0.1 — 2026-09-30

Pomodoro Timer 2.0.1 brings version 2.0 to **Ubuntu 24.04** and fixes two problems that affected the app on Ubuntu. Windows 11 behaves the same as in 2.0.0, released on 2026-09-28. If you already run 2.0.0 on Windows, you don't need to update.

Pomodoro Timer 2.0 lets you keep the same history on more than one computer, such as a laptop and a desktop PC, by syncing it through your own Google Drive. It also handles problems more gracefully and keeps log files to help track them down. Everything from 1.0 works as before, and sync is optional: if you don't turn it on, the app stays fully offline.

This release is for **Windows 11** and **Ubuntu 24.04**. Both have the same features and share one history format. You can sync a Windows PC and an Ubuntu laptop through the same Google account.

### Fixed on Ubuntu

- **The app no longer freezes at startup.** The window stayed on **Syncing…** and never opened your timer or history. It now opens normally, whether or not sync is set up.
- **The app can now show its hourglass icon in the dock and Alt-Tab.** Ubuntu showed a generic icon instead. Adding a launcher entry once fixes this. See [Showing the app icon in the Ubuntu dock](#showing-the-app-icon-in-the-ubuntu-dock).
- If the desktop's notification service doesn't respond, the app now starts anyway, with completion notifications turned off, instead of waiting indefinitely.

### What's new in 2.0

**Now on Ubuntu**

- Version 2 is now available for Ubuntu 24.04 (64-bit).
- Timer, stopwatch, history, notifications, the completion chime, and Google Drive sync all work the same as on Windows.

**Sync your history through Google Drive**

- Click **Sign in** in the title bar and sign in with your Google account. Your browser opens for Google's sign-in page. If it doesn't, the app shows a link you can open yourself.
- The app asks only for permission to use the files it creates itself. It can't see anything else in your Google Drive.
- Your history is stored in your Drive as `PomodoroTimer/pomodoro.db`. The app creates the folder for you.
- The app syncs when it starts and when you close it. Closing can take a few seconds while it uploads your latest sessions.
- The title bar shows the sync status, for example **Synced 10:42**, **Offline — will sync later**, or **Needs sign-in**. Press **Retry** there after a problem.
- If you're offline, the app works as normal with the data on your computer and syncs the next time it can.

**Use one computer at a time**

Sync is built for using the app on one computer at a time. Close it on one computer before you open it on another. The app never merges two histories.

If both computers have changes the other doesn't have, the app asks what to do:

- **Keep this device's data:** upload it to Google Drive. The Google Drive copy stays in Drive's version history.
- **Keep cloud data:** download the Google Drive copy. This computer's data is backed up first.
- **Work offline for now:** change nothing and ask again next time the app starts.

**Your data is protected**

- The app never replaces your data without asking when both sides have changed.
- Before your data is ever replaced by a download, a backup is saved next to it. The five most recent backups are kept.
- A download is checked before it's used. If it's damaged, your own data is left alone.
- Google Drive keeps earlier versions of the synced file, so an older copy can be restored from Drive if you need it.

**Clearer errors and log files**

- When something goes wrong, you see a short, plain message, and the app keeps running wherever it can.
- If the history file can't be read or written, for example because the disk is full, the app tells you and never deletes your data.
- If a notification or the chime fails, your session is still saved.
- The app writes a log file each day next to your history file. The logs never contain your Google password or sign-in details.

### Setting up sync

For sync to work, the app folder needs a `client_secret.json` file, which links the app to a Google Cloud project. If the file is missing, the title bar shows **Sync not configured** and the app works offline as before. [HOWTO.md](HOWTO.md#setting-up-google-drive-sync) explains how to create the Google Cloud project and the file. You do this once. The same file works for both Windows and Ubuntu, so copy it into each computer's app folder.

### Upgrading from 1.0 or 2.0.0

- Your history carries over. Replace the old app folder with the new one and leave your data file where it is.
- **If you use more than one computer:** sign in on the computer whose history you want to keep first. Its history is uploaded to Google Drive. When you then sign in on a second computer that already has sessions of its own, the app asks which history to keep. Sessions from 1.0 on different computers can't be combined.

### Download and run

There's no installer. Each download is a folder containing the app and everything it needs, including .NET, so you don't have to install anything else. Keep all the files in the folder together. The app needs the files next to it, including `client_secret.json` if you use sync.

#### Windows 11

Put the `win-x64` folder anywhere (unzip it first if you received a zip) and run `PomodoroTimer.App.exe`.

#### Ubuntu 24.04

Put the `linux-x64` folder anywhere (unzip it first if you received a zip) and run `PomodoroTimer.App` from a terminal:

```bash
cd linux-x64
./PomodoroTimer.App
```

If it doesn't start, the file may have lost its "executable" permission while being copied or unzipped. Restore it once with `chmod +x PomodoroTimer.App`.

#### Showing the app icon in the Ubuntu dock

Ubuntu shows a generic icon in the dock and in Alt-Tab until the app has a launcher entry. If you have the project's source code, run this once to add one:

```bash
./scripts/install-desktop-entry.sh /path/to/linux-x64/PomodoroTimer.App
```

"Pomodoro Timer" then appears in your app grid with its hourglass icon, and you can pin it to the dock. Run the command again if you move the folder. See [HOWTO.md](HOWTO.md#showing-the-app-icon-on-ubuntu) for details.

### Your data

Your history is stored in one file on your computer:

| System | Location |
|---|---|
| Windows | `%LOCALAPPDATA%\PomodoroTimer\pomodoro.db` |
| Ubuntu | `~/.local/share/PomodoroTimer/pomodoro.db` |

If you turn on sync, a copy of this file is also kept in your Google Drive. Nothing else is uploaded. The same folder on your computer also holds:

| File | What it is |
|---|---|
| `sync-state.json` | Notes on when this computer last synced |
| `google-token` | Your Google sign-in for this computer. Delete this folder to sign out. |
| `pomodoro-<date>.log` | Daily log files. The last 14 are kept. |
| `pomodoro.db.bak-<date-time>` | Backups made before your data is replaced by a download. The last five are kept. |

On Ubuntu, `~/.local/share` is a hidden folder. In the Files app, press **Ctrl+H** to show hidden folders.

Each user account on a computer has its own history. To start fresh, close the app and delete `pomodoro.db`. If you use sync, also delete `sync-state.json` here and the `PomodoroTimer` folder in your Google Drive. Otherwise the app downloads your history again.
