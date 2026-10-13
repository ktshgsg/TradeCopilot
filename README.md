# TradeCopilot

A local-first AI trading copilot for Windows. Reads the numbers on your
chart, understands Hinglish and English, answers by voice or text — all
offline, no API keys, no cloud.

## What it does

- Watches your screen continuously (2 FPS, change-gated DXGI capture)
- Reads on-chart indicator values via Windows OCR (RSI, MACD, price, volume)
- Applies a rule engine for common trading questions (overbought, breakout,
  support break, MACD cross)
- Answers freeform questions with a local Qwen2.5-1.5B LLM
- Speaks answers in Hindi via Piper TTS
- Listens in Hindi/Hinglish via Whisper ASR
- Caches answers semantically — repeat questions return in under 100 ms
- Excludes banking windows, skips sensitive OCR content, encrypts the cache
- Logs every action to a local audit file with no content leakage

## Privacy

All screen capture, OCR, LLM inference, and voice processing happen on
your machine. No data leaves your PC. No telemetry. No accounts.

See [PRIVACY.md](PRIVACY.md) for the full statement.

## Hardware requirements

| Feature | Minimum | Recommended |
|---|---|---|
| Capture + OCR + rules + cache | Any modern CPU | Any modern CPU |
| Hinglish LLM (Qwen2.5-1.5B) | 4 GB RAM, 2 cores | 8 GB RAM, 4 cores |
| Whisper ASR (voice input) | 2 cores | 4 cores (5× faster) |
| Piper TTS (voice output) | Any modern CPU | Any modern CPU |
| Vision / chart image analysis | NVIDIA GPU, 8+ GB VRAM | RTX 3060+ |

On CPU-only machines, the vision engine disables automatically and vision
queries route to the LLM + OCR path (which reads on-screen indicator
values). Vision is a roadmap feature — the engine is implemented and
documented, enabled on GPU hardware.

## Quick start

Prerequisites:

    winget install Microsoft.DotNet.SDK.8
    winget install Python.Python.3.11

Clone, download models, run:

    git clone https://github.com/YOURUSER/TradeCopilot.git
    cd TradeCopilot
    .\setup.ps1
    .\run.ps1

setup.ps1 downloads ~2 GB of models, installs Piper TTS, and builds the
solution. run.ps1 starts the service and the overlay.

See [docs/SETUP.md](docs/SETUP.md) for manual steps.

## Hotkeys

| Key | Action |
|---|---|
| Ctrl+Space | Show / hide overlay |
| Ctrl+Alt+Space | Push-to-talk (record voice) |
| Ctrl+Shift+P | Pause / resume screen capture |
| Ctrl+Shift+X | Panic — stop everything |
| Esc | Hide overlay |

## Architecture

    Screen → DXGI capture (2 FPS)
           → Change gate (perceptual hash)
           → OCR (Windows.Media.Ocr)
           → Rule engine (RSI, MACD, patterns)
           → Semantic cache (sqlite-vec)
           → Local LLM (Qwen2.5-1.5B via LLamaSharp)
           → Voice output (Piper Hindi TTS)
           ↑
    Voice input (Whisper) ──────┘

All components run as a Windows Service plus a WPF overlay, communicating
over a local named pipe.

## Development

    dotnet build -c Release
    dotnet test -c Release

Requires .NET SDK 8.0.4xx (pinned via global.json). 387 tests, all green.

## License

MIT — see [LICENSE.txt](LICENSE.txt).
