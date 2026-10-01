# Release notes

## 2.2.1 — 2026-10-01

Pomodoro Timer 2.2.1 lets you pick session names from a list you keep yourself, and syncs that list between your computers along with your history. It also adds a **Sync** button and saves a running session if you close the app. Your history carries over unchanged.

This release is for **Windows 11** and **Ubuntu 24.04**. Both have the same features and share one history format.

> **Before your first session:** every session now needs a name from your names list, and the list starts out empty. After you start 2.2.1 once, add your names to `names.txt` and restart the app. See [Your names list](#your-names-list) below.

### What's new since 2.1.2

**Your names list**

- Session names now come from `names.txt`, a plain text file in the same folder as your history, with one name per line. Edit it in any text editor, such as Notepad on Windows or Text Editor on Ubuntu.
- The app creates the file, empty, the first time 2.2.1 starts. Changes to it show up the next time you start the app.
- The **Name** box is now a search box. Type a letter or two and it lists the names that contain them, whether you type upper or lower case.
- A name is required, and it must be one from your list. If it isn't, the app tells you so and shows where `names.txt` is.
- Removing a name from the list doesn't change your history. Past sessions keep their names.

**Your names list syncs too**

- If you use Google Drive sync, `names.txt` is kept in your Drive next to your history, as `PomodoroTimer/names.txt`. Every computer gets the same list.
- It follows the same safety rules as your history. A backup is made before the list is replaced by a download, and a downloaded list is checked before it's used. If the list was changed on two computers without syncing in between, the app asks which one to keep: **Keep this device's names list**, **Keep cloud names list**, or **Work offline for now**.

**Sync button**

- A new **Sync** button after **History** syncs your history and names list with Google Drive whenever you like. You don't have to wait for the app to start or close.
- It does the same checks as at startup, and asks you to sign in if needed. It's greyed out while a sync is already running.
- The app still syncs on its own when it starts and when you close it.

**Closing the app during a session**

- Before, closing the app while a timer or stopwatch was running lost that session.
- Now the app asks first, for example **A timer is running — stop and close?**
  - **Stop and close** saves the session exactly as if you had pressed **Stop**, uploads it, and then closes. A timer is saved as stopped early.
  - **Keep running** cancels the close, and your session carries on.
- If the computer is shutting down or logging off, the app saves the session and syncs without asking.

**For developers**

A development build (`dotnet run` or `scripts/run.*`) has its own names list in the `PomodoroTimer-Dev` folder. In Google Drive it syncs `names-dev.txt` and never touches your real `names.txt`. Add a few names to the dev `names.txt` before starting a session. See [HOWTO.md](HOWTO.md#running-locally).

### Your names list

The file is in the same folder as your history:

| System | Location |
|---|---|
| Windows | `%LOCALAPPDATA%\PomodoroTimer\names.txt` |
| Ubuntu | `~/.local/share/PomodoroTimer/names.txt` |

Put one name on each line:

```
Deep work
Reading
Email
```

Blank lines and spaces around a name are ignored. A name listed twice appears once.

**If you use more than one computer,** fill in the list on one computer, then close the app there so the list is uploaded. The other computers download it the next time they start. If two computers each get their own list before either has synced, the app asks which list to keep.

## 2.1.2 — 2026-10-01

Pomodoro Timer 2.1.2 adds a **Ratio** table to the Dashboard. The table shows how your time splits between work, study, and breaks. This release also adds a 6-month period and renames the periods on the Dashboard and History pages to read more naturally. Everything else works as in 2.0.1, including Google Drive sync, and your history carries over unchanged.

This release is for **Windows 11** and **Ubuntu 24.04**. Both have the same features and share one history format. You can sync a Windows PC and an Ubuntu laptop through the same Google account.

### What's new in 2.1.2

**Ratio table on the Dashboard**

- A new **Ratio** table appears below **Daily averages**. For each period, it shows what share of your recorded time went to Work, Study, and Break, for example 50% / 20% / 30%.
- The three percentages in a row always add up to 100%.
- A period with no recorded time shows **—** instead of percentages.

**Clearer period names**

The Dashboard and History pages now use the same six periods:

| Before | Now |
|---|---|
| Last 7 days | Last 7 days |
| Last 14 days | Last 14 days |
| Last 30 days | Last 1 month |
| Last 90 days | Last 3 months |
| Last 180 days | Last 6 months |
| Last 365 days | Last 1 year |

The periods still count back a fixed number of days from today. For example, **Last 1 month** is the last 30 days, not the current calendar month.

**More periods in each table**

- **Totals** now includes **Last 6 months**.
- **Daily averages** now includes **Last 6 months** and **Last 1 year**. Before, it stopped at 3 months.
- The **Period** filter on the History page now includes **Last 3 months**.

**For developers**

If you build the app from source, a development build (`dotnet run` or `scripts/run.*`) now keeps its own history. It no longer shares the history of the app you use day to day. Its files go to a separate `PomodoroTimer-Dev` folder. In Google Drive, it syncs `pomodoro-dev.db` in the same `PomodoroTimer` folder and never touches your real `pomodoro.db`. The first development run asks you to sign in to Google once. Published builds are not affected. See [HOWTO.md](HOWTO.md#running-locally).

### Earlier fixes on Ubuntu (2.0.1)

- **The app no longer freezes at startup.** The window stayed on **Syncing…** and never opened your timer or history. It now opens normally, whether or not sync is set up.
- **The app can now show its hourglass icon in the dock and Alt-Tab.** Ubuntu showed a generic icon instead. Adding a launcher entry once fixes this. See [Showing the app icon in the Ubuntu dock](#showing-the-app-icon-in-the-ubuntu-dock).
- If the desktop's notification service doesn't respond, the app now starts anyway, with completion notifications turned off, instead of waiting indefinitely.

### What's new in 2.0

Pomodoro Timer 2.0 lets you keep the same history on more than one computer, such as a laptop and a desktop PC, by syncing it through your own Google Drive. It also handles problems more gracefully and keeps log files to help track them down. Everything from 1.0 works as before, and sync is optional: if you don't turn it on, the app stays fully offline.

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

### Upgrading

- **From 2.1.x:** replace the old app folder with the new one, and copy your `client_secret.json` into it if you use sync. Your history, sync settings, and Google sign-in carry over. Start the app once, add your names to `names.txt`, and restart it (see [Your names list](#your-names-list)).
- **From 2.0.0 or 2.0.1:** replace the old app folder with the new one. Keep your `client_secret.json` and copy it into the new folder if you use sync. Your history, sync settings, and Google sign-in carry over.
- **From 1.0:** your history carries over. Replace the old app folder with the new one and leave your data file where it is.
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

If you turn on sync, a copy of this file is also kept in your Google Drive, along with your names list. Nothing else is uploaded. The same folder on your computer also holds:

| File | What it is |
|---|---|
| `names.txt` | Your names list, one name per line. Edit it yourself. |
| `sync-state.json`, `sync-state-names.json` | Notes on when this computer last synced your history and your names list |
| `google-token` | Your Google sign-in for this computer. Delete this folder to sign out. |
| `pomodoro-<date>.log` | Daily log files. The last 14 are kept. |
| `pomodoro.db.bak-<date-time>`, `names.txt.bak-<date-time>` | Backups made before your history or names list is replaced by a download. The last five of each are kept. |

On Ubuntu, `~/.local/share` is a hidden folder. In the Files app, press **Ctrl+H** to show hidden folders.

Each user account on a computer has its own history. To start fresh, close the app and delete `pomodoro.db`. If you use sync, also delete `sync-state.json` here and the `PomodoroTimer` folder in your Google Drive. Otherwise the app downloads your history again. Deleting that folder also removes the names list from Drive. Keep your local `names.txt`, and choose **Upload this device's names list** when the app says the Google Drive copy is missing.
