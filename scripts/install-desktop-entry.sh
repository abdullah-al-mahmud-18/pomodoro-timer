#!/usr/bin/env bash
# Adds Pomodoro Timer to the Ubuntu app launcher so GNOME shows its icon in the dock, Alt-Tab, and Activities.
# GNOME ignores the icon a window sets on itself; it only uses the icon from a matching .desktop file.
# Usage: scripts/install-desktop-entry.sh [path/to/PomodoroTimer.App]   (default: publish/linux-x64/PomodoroTimer.App)
# Run it again after moving the published folder. Remove with: scripts/install-desktop-entry.sh --uninstall
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
ICON_PATH="$DATA_HOME/icons/hicolor/512x512/apps/pomodoro-timer.png"
DESKTOP_PATH="$DATA_HOME/applications/pomodoro-timer.desktop"

if [[ "${1:-}" == "--uninstall" ]]; then
    rm -f "$DESKTOP_PATH" "$ICON_PATH"
    echo "Removed $DESKTOP_PATH and $ICON_PATH"
    exit 0
fi

EXECUTABLE="$(realpath "${1:-$ROOT/publish/linux-x64/PomodoroTimer.App}")"
if [[ ! -x "$EXECUTABLE" ]]; then
    echo "Executable not found: $EXECUTABLE" >&2
    echo "Publish first with scripts/publish.sh linux-x64, or pass the path to PomodoroTimer.App." >&2
    exit 1
fi

install -D -m 644 "$ROOT/src/PomodoroTimer.App/Assets/hourglass.png" "$ICON_PATH"

mkdir -p "$(dirname "$DESKTOP_PATH")"
# StartupWMClass must match the window's WM_CLASS (the assembly name) so GNOME links the running window to this entry.
cat > "$DESKTOP_PATH" <<EOF
[Desktop Entry]
Type=Application
Name=Pomodoro Timer
Comment=Pomodoro timer and stopwatch
Exec="$EXECUTABLE"
Path=$(dirname "$EXECUTABLE")
Icon=$ICON_PATH
Terminal=false
Categories=Utility;
StartupWMClass=PomodoroTimer.App
EOF

command -v update-desktop-database > /dev/null && update-desktop-database "$(dirname "$DESKTOP_PATH")" || true

echo "Installed $DESKTOP_PATH -> $EXECUTABLE"
