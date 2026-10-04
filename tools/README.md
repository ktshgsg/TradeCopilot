# Tools

Developer tools and third-party executables live here. The folder contents are
git-ignored (`/tools/*` in `.gitignore`) — binaries are downloaded, not committed.
Only `tools/README.md` and `tools/.gitkeep` are tracked.

## Apidump

Reflection-based API dumper used to verify third-party APIs (LLamaSharp,
Whisper.net, NAudio) against the exact assemblies in the NuGet cache instead of
guessing method names.

```powershell
cd tools/Apidump
dotnet run
# writes bin/Debug/net8.0/dump2.txt
```

## Piper TTS — `tools/piper/piper.exe`

Used by `PiperSpeechSynthesizer` for Hindi text-to-speech.

1. Download `piper_windows_amd64.zip` from
   <https://github.com/rhasspy/piper/releases>.
2. Extract `piper.exe` (and `piper_phonemize.exe` / DLLs if present) into
   `tools/piper/`.
3. Download the Hindi voice from <https://huggingface.co/rhasspy/piper-voices>:
   - `hi/hi_IN/rohan/medium/hi_IN-rohan-medium.onnx`
   - `hi/hi_IN/rohan/medium/hi_IN-rohan-medium.onnx.json`

   Place both in `models/piper/`.

### Manual smoke test

From the repository root, in PowerShell:

```powershell
"नमस्ते RSI overbought है" | tools/piper/piper.exe --model models/piper/hi_IN-rohan-medium.onnx --output_file test.wav
```

The command should produce `test.wav` (tens of KB). PowerShell's default pipe
encoding is not UTF-8, so if the Devanagari text is mangled, write it explicitly:

```powershell
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
"नमस्ते RSI overbought है" | tools/piper/piper.exe --model models/piper/hi_IN-rohan-medium.onnx --output_file test.wav
```

Then play it:

```powershell
Add-Type -AssemblyName PresentationCore
(New-Object Media.MediaPlayer).Open((Resolve-Path test.wav)).Play()
```

When either `piper.exe` or the voice model is missing, `PiperSpeechSynthesizer`
logs a warning, reports `Enabled == false`, and `SpeakAsync` is a no-op.