#Requires -Version 5.1
<#
.SYNOPSIS
    One-shot setup for TradeCopilot: downloads the models (~2 GB), installs
    the Piper TTS binary, and builds the solution in Release.
#>
$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

Write-Host '==> Checking prerequisites...'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'ERROR: dotnet not found. Install the .NET SDK first: winget install Microsoft.DotNet.SDK.8' -ForegroundColor Red
    exit 1
}
if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
    Write-Host 'ERROR: python not found. Install Python 3.11 first: winget install Python.Python.3.11' -ForegroundColor Red
    exit 1
}

Write-Host '==> Ensuring huggingface_hub is installed...'
python -m pip install --upgrade huggingface_hub
if ($LASTEXITCODE -ne 0) { Write-Host 'ERROR: pip install of huggingface_hub failed.' -ForegroundColor Red; exit 1 }

Write-Host '==> Creating folders...'
New-Item -ItemType Directory -Force -Path models | Out-Null
New-Item -ItemType Directory -Force -Path models\piper | Out-Null
New-Item -ItemType Directory -Force -Path data | Out-Null
New-Item -ItemType Directory -Force -Path tools\piper | Out-Null

Write-Host '==> Downloading LLM (~1 GB)...'
hf download Qwen/Qwen2.5-1.5B-Instruct-GGUF qwen2.5-1.5b-instruct-q4_k_m.gguf --local-dir models
if ($LASTEXITCODE -ne 0) { Write-Host 'ERROR: LLM download failed.' -ForegroundColor Red; exit 1 }

Write-Host '==> Downloading Whisper ASR model (~150 MB)...'
hf download ggerganov/whisper.cpp ggml-base.bin --local-dir models
if ($LASTEXITCODE -ne 0) { Write-Host 'ERROR: Whisper model download failed.' -ForegroundColor Red; exit 1 }

Write-Host '==> Downloading Hindi TTS voice (~65 MB)...'
hf download rhasspy/piper-voices hi/hi_IN/rohan/medium/hi_IN-rohan-medium.onnx --local-dir models\piper
if ($LASTEXITCODE -ne 0) { Write-Host 'ERROR: Piper voice download failed.' -ForegroundColor Red; exit 1 }
hf download rhasspy/piper-voices hi/hi_IN/rohan/medium/hi_IN-rohan-medium.onnx.json --local-dir models\piper
if ($LASTEXITCODE -ne 0) { Write-Host 'ERROR: Piper voice JSON download failed.' -ForegroundColor Red; exit 1 }
if (Test-Path models\piper\hi) {
    Move-Item -Path 'models\piper\hi\hi_IN\rohan\medium\*' -Destination 'models\piper\' -Force
    Remove-Item -Recurse -Force models\piper\hi
}

Write-Host '==> Downloading Piper TTS binary...'
$piperZipUrl = 'https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip'
$piperZip = Join-Path $env:TEMP 'piper_windows_amd64.zip'
Invoke-WebRequest -Uri $piperZipUrl -OutFile $piperZip
Expand-Archive -Path $piperZip -DestinationPath tools\piper -Force
if (Test-Path tools\piper\piper\piper.exe) {
    # Extracted into a nested folder - flatten it.
    Get-ChildItem tools\piper\piper | Move-Item -Destination tools\piper\ -Force
    Remove-Item -Recurse -Force tools\piper\piper
}
if (-not (Test-Path tools\piper\piper.exe)) {
    Write-Host 'ERROR: piper.exe not found after extraction; check tools/piper/.' -ForegroundColor Red
    exit 1
}

Write-Host '==> Building the solution (Release)...'
dotnet build -c Release
if ($LASTEXITCODE -ne 0) { Write-Host 'ERROR: build failed.' -ForegroundColor Red; exit 1 }

Write-Host ''
Write-Host 'Setup complete. Start everything with: .\run.ps1'
