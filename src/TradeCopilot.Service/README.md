# TradeCopilot Service

Windows Service host for the TradeCopilot agent. Owns screen capture → OCR/pattern
processing → agent state, the semantic cache, the local LLM, and speech I/O, and exposes
them to the UI over a named pipe.

## Running

```powershell
dotnet run --project src/TradeCopilot.Service -c Release
```

Startup logs these lines in order (the last one is the readiness signal scripts grep for):

```
TradeCopilot starting, version 0.1.0
Cache init: enabled=true in 12ms
LLM init: enabled=false in 3ms          # true when models/qwen2.5-3b-instruct-q4_k_m.gguf exists
ASR init: enabled=false in 1ms          # true when models/ggml-medium.bin exists
TTS init: enabled=false in 0ms          # true when tools/piper/piper.exe + voice exist
Engine init finished in 18ms (cache ok, llm enabled=False, asr enabled=False, tts enabled=False)
Warm-up completed in 143ms
Pipe server listening on TradeCopilot.Pipe.
Ready
```

Missing models are **not** fatal: each engine logs `enabled=false` and the service still
reaches `Ready`, serving deterministic rule answers and the offline LLM fallback string.

Stop with **Ctrl+C**; `Stopped` is logged and shutdown completes in well under 5 s.

## Configuration

All settings live under the `Agent` section of `appsettings.json`:

| Key | Default | Meaning |
| --- | --- | --- |
| `Capture:X/Y/Width/Height` | `0` | Desktop region; width/height `0` means the full output. |
| `Capture:Fps` | `2` | Frame-rate cap (OCR is expensive; 2 fps is plenty for a chart). |
| `Models:EmbeddingPath` | `models/embedding_model.onnx` | Used once the ONNX provider is wired in. |
| `Models:LlmPath` | `models/qwen2.5-3b-instruct-q4_k_m.gguf` | GGUF chat model. |
| `Models:WhisperPath` | `models/ggml-medium.bin` | GGML ASR model. |
| `Models:PiperExePath` | `tools/piper/piper.exe` | Piper executable. |
| `Models:PiperVoicePath` | `models/piper/hi_IN-rohan-medium.onnx` | Piper voice. |
| `Cache:DbPath` | `data/cache.db` | SQLite file. |
| `Cache:SimilarityThreshold` | `0.88` | Minimum cosine similarity for a hit. |
| `Cache:MaxAgeDays` | `7` | Retention for rarely-hit entries. |
| `Pipe:Name` | `TradeCopilot.Pipe` | Pipe the UI connects to. |

> Note: the DXGI duplication surface (and therefore the crop rectangle) is fixed when
> `DxgiScreenCapture` is constructed, so `Pipe:Name`/`Capture:*` changes need a restart;
> `resume_capture` uses the configured values.

## Pipe protocol

Newline-delimited JSON, one request per line, one response per line. Message-mode framing
is not used, so a client only needs a reader/writer pair.

Requests:

```json
{"type":"query","text":"RSI kya hai?"}
{"type":"status"}
{"type":"pause_capture"}
{"type":"resume_capture"}
{"type":"transcribe","audioBase64":"<base64 PCM16 16 kHz mono>"}
{"type":"panic"}
```

Responses:

```json
{"type":"answer","text":"...","language":"hinglish","source":"llm","elapsedMs":42}
{"type":"answer","text":"...","source":"asr"}
{"type":"status","captureActive":true,"framesProcessed":1234,"uptimeSec":300,"version":"0.1.0"}
{"type":"ok"}
{"type":"error","message":"unknown type 'foo'"}
```

Null fields are omitted. `source` is one of `cache`, `rule`, `llm`, `asr`, or `empty`.
`panic` stops capture and clears agent state.

> **Client tip:** wrap the pipe in a single `StreamWriter` **or** a single `StreamReader`
> with `leaveOpen: true`. Wrapping the same `NamedPipeClientStream` in a
> `StreamWriter`/`StreamReader` pair that both own the stream disposes it and the next
> write fails with `ObjectDisposedException: Cannot access a closed pipe`.

## Manual smoke check

Not an automated test — run it by hand against a live service.

```powershell
$p = New-Object System.IO.Pipes.NamedPipeClientStream(".", "TradeCopilot.Pipe",
     [System.IO.Pipes.PipeDirection]::InOut)
$p.Connect(2000)
$w = New-Object System.IO.StreamWriter($p)
$r = New-Object System.IO.StreamReader($p)
$w.WriteLine('{"type":"status"}'); $w.Flush()
$r.ReadLine()
```

Expected output (values differ run to run):

```
{"type":"status","captureActive":true,"framesProcessed":0,"uptimeSec":12,"version":"0.1.0"}
```

Then a query round-trip:

```powershell
$w.WriteLine('{"type":"query","text":"What is RSI?"}'); $w.Flush()
$r.ReadLine()
# {"type":"answer","text":"...","language":"en","source":"llm","elapsedMs":812}
```

Note this PowerShell snippet keeps a single `StreamWriter` and a single `StreamReader` on
the same pipe; if you close one, dispose `$p` and reconnect.

## Tests

`Tests/PipeServerTests.cs` covers the protocol end-to-end over a real pipe with its own
host and a unique pipe name (`TradeCopilot.TestPipe.{guid}`), so it never collides with a
running service. The tests skip when `Environment.UserInteractive` is false (headless CI).

```powershell
dotnet test src/TradeCopilot.Tests -c Release --filter "Category=Integration"
```