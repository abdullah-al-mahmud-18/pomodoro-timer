#!/usr/bin/env bash
# Publishes a self-contained, single-file Release build.
# Usage: scripts/publish.sh [win-x64 | linux-x64 | all]   (default: the current platform)
# Output: publish/<runtime>/
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/PomodoroTimer.App/PomodoroTimer.App.csproj"

publish() {
    local runtime="$1"
    local framework
    case "$runtime" in
        win-x64) framework="net10.0-windows10.0.19041.0" ;;
        linux-x64) framework="net10.0" ;;
    esac

    local output="$ROOT/publish/$runtime"
    echo "Publishing $runtime -> $output"
    dotnet publish "$PROJECT" \
        -c Release \
        -r "$runtime" \
        -f "$framework" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -o "$output"
}

if [[ $# -gt 0 ]]; then
    TARGET="$1"
else
    case "$(uname -s)" in
        MINGW* | MSYS* | CYGWIN*) TARGET="win-x64" ;;
        *) TARGET="linux-x64" ;;
    esac
fi

case "$TARGET" in
    win-x64 | linux-x64) publish "$TARGET" ;;
    all)
        publish win-x64
        publish linux-x64
        ;;
    *)
        echo "Unknown target '$TARGET'. Use win-x64, linux-x64, or all." >&2
        exit 1
        ;;
esac
