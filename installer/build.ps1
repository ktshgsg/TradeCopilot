<#
.SYNOPSIS
    Builds the self-contained TradeCopilot installer.

.DESCRIPTION
    Publishes the UI and the Windows service as self-contained win-x64 binaries
    (no .NET runtime required on the target machine) into installer\out\, then
    compiles installer\TradeCopilot.iss with Inno Setup. The version comes from
    version.txt at the repository root.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\build.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$installerDir = $PSScriptRoot
$repoRoot = Split-Path -Parent $installerDir

# global.json pins the per-user SDK; prefer it over anything on PATH.
$localDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { 'dotnet' }

$version = (Get-Content (Join-Path $repoRoot 'version.txt') -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "version.txt is empty; expected a version like 0.2.0."
}
Write-Host "=== TradeCopilot $version ($Configuration, self-contained win-x64) ==="

$publishRoot = Join-Path $installerDir 'out'
$uiOut = Join-Path $publishRoot 'ui'
$serviceOut = Join-Path $publishRoot 'service'
Remove-Item $publishRoot -Recurse -Force -ErrorAction SilentlyContinue

# PublishSelfContained=true activates the win-x64 / ReadyToRun block in
# Directory.Build.props, so no -r flag is needed here.
$projects = @(
    @{ Name = 'UI';      Path = Join-Path $repoRoot 'src\TradeCopilot.UI\TradeCopilot.UI.csproj';          Out = $uiOut },
    @{ Name = 'Service'; Path = Join-Path $repoRoot 'src\TradeCopilot.Service\TradeCopilot.Service.csproj'; Out = $serviceOut }
)
foreach ($project in $projects) {
    Write-Host "--- Publishing $($project.Name) ---"
    & $dotnet publish $project.Path -c $Configuration -p:PublishSelfContained=true -o $project.Out
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $($project.Name) (exit code $LASTEXITCODE)."
    }
}

# --- Locate Inno Setup 6 (ISCC.exe) -----------------------------------------
$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
$iscc = $isccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) {
    throw 'Inno Setup 6 (ISCC.exe) not found. Install it from https://jrsoftware.org/isdl.php.'
}

# --- Compile the installer ---------------------------------------------------
$issPath = Join-Path $installerDir 'TradeCopilot.iss'
Write-Host "--- Compiling installer ($issPath) ---"
& $iscc "/DVersion=$version" $issPath
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed (exit code $LASTEXITCODE)."
}

$setupExe = Join-Path $publishRoot "TradeCopilot-Setup-$version.exe"
if (Test-Path $setupExe) {
    $sizeMb = (Get-Item $setupExe).Length / 1MB
    Write-Host ('=== Installer created: {0} ({1:N1} MB) ===' -f $setupExe, $sizeMb)
} else {
    throw "Expected installer not found at $setupExe."
}
