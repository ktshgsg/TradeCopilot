# Setup

## Automatic (recommended)

    git clone https://github.com/YOURUSER/TradeCopilot.git
    cd TradeCopilot
    .\setup.ps1     # downloads models, builds
    .\run.ps1       # starts service + overlay

## Manual

### 1. Install tools

    winget install Microsoft.DotNet.SDK.8
    winget install Python.Python.3.11

Restart your terminal.

### 2. Clone and build

    git clone https://github.com/YOURUSER/TradeCopilot.git
    cd TradeCopilot
    dotnet build -c Release

### 3. Download models (~2 GB)

    pip install --upgrade huggingface_hub

    # LLM (~1 GB)
    hf download Qwen/Qwen2.5-1.5B-Instruct-GGUF qwen2.5-1.5b-instruct-q4_k_m.gguf --local-dir models

    # Whisper ASR (~150 MB)
    hf download ggerganov/whisper.cpp ggml-base.bin --local-dir models

    # Hindi TTS voice (~65 MB)
    hf download rhasspy/piper-voices hi/hi_IN/rohan/medium/hi_IN-rohan-medium.onnx --local-dir models\piper
    hf download rhasspy/piper-voices hi/hi_IN/rohan/medium/hi_IN-rohan-medium.onnx.json --local-dir models\piper
    Move-Item "models\piper\hi\hi_IN\rohan\medium\*" "models\piper\"
    Remove-Item -Recurse "models\piper\hi"

### 4. Piper TTS binary

Download:
    https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip

Extract to tools/piper/. Flatten if it extracts into tools/piper/piper/.

Verify:
    Test-Path tools/piper/piper.exe    # must return True

### 5. Run

Terminal 1:
    dotnet run --project src/TradeCopilot.Service

Wait for:
    Ready

Terminal 2:
    dotnet run --project src/TradeCopilot.UI

The overlay appears top-right. Ask a Hinglish question.

## Troubleshooting

| Problem | Fix |
|---|---|
| dotnet: command not found | Restart terminal after installing SDK |
| LLM init: enabled=False | Model file missing — re-run setup.ps1 |
| Vision init: enabled=False | Expected on CPU-only machines. Vision requires an NVIDIA GPU. |
| unable to open database file | New-Item -ItemType Directory -Force data |
| Mic doesn't record | Windows Settings → Privacy → Microphone → allow desktop apps |
| hf: command not found | python -m pip install --upgrade huggingface_hub |
| Pipe connection error | Another instance is running — kill it in Task Manager |
