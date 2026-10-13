# Privacy

TradeCopilot is local-first by design. Nothing leaves your machine.

## What it captures

- Your screen, continuously at 2 FPS while the service is running
- Your microphone, only while push-to-talk (Ctrl+Alt+Space) is active

## What it does with that data

- Screen frames are processed in memory for OCR (indicator values) and
  discarded after a short ring buffer. Raw frames are not written to disk.
- OCR text and pattern labels are cached locally in an encrypted SQLite
  database (data/cache.db). The encryption key is derived from your
  Windows user account via DPAPI.
- Microphone audio is streamed directly to Whisper for transcription and
  discarded immediately after. Audio is never stored.
- An audit log at data/audit.log records events (capture started, query
  answered, panic triggered) with only short reason labels — never the
  raw OCR text, window titles, or query content.

## What it never does

- Never sends screen data, OCR text, audio, or queries to any server
- Never uses an API key — the LLM, ASR, and TTS all run on your machine
- Never stores passwords or credentials — sensitive windows (banking,
  wallet, keychain) are excluded from capture by pattern matching
- Never runs analytics or telemetry

## Your controls

- Ctrl+Shift+P pauses screen capture immediately
- Ctrl+Shift+X (panic) stops capture, mic, and TTS in one action
- Exclusion list in settings lets you blacklist window titles
- Purge cache button clears the local semantic cache

## Data you can delete

Everything is in `%LOCALAPPDATA%\TradeCopilot\` (or the repo's `data/`
folder when running from source). Delete that folder to remove all
persisted state.
