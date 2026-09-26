# Publishes a self-contained, single-file Release build.
# Usage: scripts/publish.ps1 [-Runtime win-x64 | linux-x64 | all]   (default: the current platform)
# Output: publish/<runtime>/
param(
    [ValidateSet('win-x64', 'linux-x64', 'all')]
    [string]$Runtime
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/PomodoroTimer.App/PomodoroTimer.App.csproj'

function Publish-Runtime([string]$rid) {
    $framework = if ($rid -eq 'win-x64') { 'net10.0-windows10.0.19041.0' } else { 'net10.0' }
    $output = Join-Path $root "publish/$rid"

    Write-Host "Publishing $rid -> $output"
    dotnet publish $project `
        -c Release `
        -r $rid `
        -f $framework `
        --self-contained true `
        -p:PublishSingleFile=true `
        -o $output

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $rid (exit code $LASTEXITCODE)."
    }
}

if (-not $Runtime) {
    # $IsWindows only exists in PowerShell 6+; Windows PowerShell 5.1 ("Desktop" edition) is always Windows.
    $onWindows = ($PSVersionTable.PSEdition -eq 'Desktop') -or $IsWindows
    $Runtime = if ($onWindows) { 'win-x64' } else { 'linux-x64' }
}

if ($Runtime -eq 'all') {
    Publish-Runtime 'win-x64'
    Publish-Runtime 'linux-x64'
}
else {
    Publish-Runtime $Runtime
}
