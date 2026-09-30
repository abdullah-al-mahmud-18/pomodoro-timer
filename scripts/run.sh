#!/usr/bin/env bash
# Runs the app locally in Debug. Extra arguments are passed through to `dotnet run`.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/PomodoroTimer.App/PomodoroTimer.App.csproj"

case "$(uname -s)" in
    MINGW* | MSYS* | CYGWIN*) FRAMEWORK="net10.0-windows10.0.19041.0" ;;
    *) FRAMEWORK="net10.0" ;;
esac

dotnet run --project "$PROJECT" -f "$FRAMEWORK" "$@"
