# Release notes

## 1.0.0 — 2026-09-27

The first release of Pomodoro Timer, a timer and stopwatch app for tracking focused work, study and breaks. This release is for **Windows 11**. It works fully offline and keeps all your data on your own computer.

### What you can do

**Time your sessions**

- **Timer:** set any duration in hours, minutes and seconds and count down to zero, with a progress bar.
- **Stopwatch:** count up with no end time.
- Pause, resume or stop either one at any time. Paused time isn't counted.
- Give each session a name, and tag it as **Work**, **Study** or **Break** before you start.
- The time always shows as `HH:MM:SS`.
- When a timer finishes, you get a desktop notification and a chime that plays five times.

**Review your history**

- Every session you finish or stop is saved with its name, type, mode, duration, status and start time.
- Filter the list by:
  - **Type:** All, Timer, Stopwatch
  - **Mode:** All, Work, Study, Break
  - **Session:** All, Completed, Stopped early
  - **Period:** All, or the last 7, 14, 30, 180 or 365 days
- The page opens on All / All / All / Last 7 days each time.
- Delete a single session with its bin icon.
- Delete every session the current filters show with **Delete Filtered Data**. It asks you to confirm first, because deleted sessions can't be recovered.

**See your totals**

- The **Dashboard** shows how much time you've spent on Work, Study and Break:
  - today
  - over the last 7, 14, 30, 90 and 365 days
  - as a daily average over the last 7, 14, 30 and 90 days
- It always counts every saved session, whatever filters you've chosen in History.

**Get around**

- **Timer**, **Dashboard** and **History** buttons appear on every page, and the page you're on is highlighted.
- A running timer or stopwatch keeps going while you look at other pages.
- The app always uses a dark theme.

### Download and run

There's no installer. The download is a folder containing the app and everything it needs, including .NET, so you don't have to install anything else.

Put the `win-x64` folder anywhere (unzip it first if you received a zip) and run `PomodoroTimer.App.exe`.

Keep all the files in the folder together. The app needs the libraries next to it.

### Your data

Your history is stored in one file on your computer and is never sent anywhere:

```
%LOCALAPPDATA%\PomodoroTimer\pomodoro.db
```

Each user account on a computer has its own history. To keep your history when you move to a new version, leave this file where it is. To start fresh, close the app and delete the file.
