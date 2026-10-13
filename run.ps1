#Requires -Version 5.1
<#
.SYNOPSIS
    Starts the TradeCopilot service and then the WPF overlay.
#>
$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'ERROR: dotnet not found on PATH. Install the .NET SDK 8.' -ForegroundColor Red
    exit 1
}

$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;" + $env:PATH

New-Item -ItemType Directory -Force -Path data | Out-Null

Write-Host 'Starting the TradeCopilot service...'
Start-Process -FilePath 'dotnet' -ArgumentList 'run','--project','src/TradeCopilot.Service','-c','Release' -WorkingDirectory $PSScriptRoot

Write-Host 'Waiting for the service to initialise...'
Start-Sleep -Seconds 10

Write-Host 'Starting the overlay...'
& dotnet run --project src/TradeCopilot.UI -c Release
