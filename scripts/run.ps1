# Runs the app locally in Debug. Extra arguments are passed through to `dotnet run`.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/PomodoroTimer.App/PomodoroTimer.App.csproj'

# $IsWindows only exists in PowerShell 6+; Windows PowerShell 5.1 ("Desktop" edition) is always Windows.
$onWindows = ($PSVersionTable.PSEdition -eq 'Desktop') -or $IsWindows
$framework = if ($onWindows) { 'net10.0-windows10.0.19041.0' } else { 'net10.0' }

dotnet run --project $project -f $framework @args
exit $LASTEXITCODE
