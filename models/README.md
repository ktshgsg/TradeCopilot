# Models

ONNX model assets live here at runtime. Binaries are **not** committed
(`/models/*` is git-ignored); only this README is tracked. Drop the files into
this folder, or point the code at another path via the detector/provider
constructor parameters.

## chart_patterns.onnx — YOLOv8 chart-pattern detector

Expected by `YoloChartPatternDetector` (default path `models/chart_patterns.onnx`).

- Architecture: YOLOv8n detection head, fine-tuned on chart screenshots.
- Classes: 11, in this order (see `PatternLabels`):
  `head_and_shoulders`, `inverse_head_and_shoulders`, `double_top`,
  `double_bottom`, `ascending_triangle`, `descending_triangle`,
  `symmetrical_triangle`, `bull_flag`, `bear_flag`, `support_break`,
  `resistance_break`.
- Input: RGB, float32, NCHW `[1, 3, 640, 640]`, values in [0, 1]; frames are
  letterboxed (aspect ratio preserved, zero padding centered).
- Output: `[1, 4 + 11, N]` (YOLOv8 default) or transposed `[1, N, 4 + 11]`;
  rows are `cx, cy, w, h` in model-space pixels followed by per-class scores.
  The detector auto-detects the layout, keeps scores >= 0.35, and applies
  per-class NMS at IoU 0.45.
- Export example:
  `yolo export model=chart_patterns.pt format=onnx opset=12 imgsz=640`.

When the file is missing or fails to load, the detector logs a warning and
returns no patterns (`Enabled == false`); the pipeline keeps running.

## embedding_model.onnx + embedding_tokenizer.json — text embeddings

Expected by `OnnxEmbeddingProvider` (default paths `models/embedding_model.onnx`
and `models/embedding_tokenizer.json`).

- Recommended: a sentence-transformers/all-MiniLM-L6-v2 style model producing
  384-dimension embeddings; vectors are L2-normalized before use.
- Tokenizer: HuggingFace `tokenizer.json`.

Current status: Microsoft.ML.Tokenizers 2.0.0 has no factory for a generic
HuggingFace `tokenizer.json`, so the provider logs a warning and serves the
deterministic `FakeEmbeddingProvider` fallback (`UsesFallback == true`). The
ONNX path activates automatically once a supported tokenizer loader exists.

## LLM

Download qwen2.5-3b-instruct-q4_k_m.gguf from
https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF
Place at models/qwen2.5-3b-instruct-q4_k_m.gguf (~2.0 GB).

## ASR — ggml-medium.bin

Expected by `WhisperSpeechRecognizer` (default path `models/ggml-medium.bin`).

- Download from https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin
  (~1.5 GB, GGML format; not the `ggml-*.bin` from another repo).
- `Whisper.net.Runtime` 1.9.1 supplies the native `whisper.dll` / `ggml-*.dll` binaries,
  so no separate native build is needed.
- Recognition is pinned to `WithLanguage("hi")` with beam size 5 and a trading-domain
  prompt, so transcripts come back in mixed Roman/Devanagari script.
- When the model is absent, the recognizer logs a warning, stays disabled, and
  `TranscribeAsync` returns an empty string; the pipeline keeps running.

## TTS (Piper)

`PiperSpeechSynthesizer` shells out to `piper.exe` and plays the generated WAV.

- Download `piper_windows_amd64.zip` from
  https://github.com/rhasspy/piper/releases and extract `piper.exe` to `tools/piper/`
  (see tools/README.md).
- Download the voice from https://huggingface.co/rhasspy/piper-voices:
  - `hi/hi_IN/rohan/medium/hi_IN-rohan-medium.onnx`
  - `hi/hi_IN/rohan/medium/hi_IN-rohan-medium.onnx.json`

  → `models/piper/`
- Text is transliterated Roman→Devanagari by `HinglishTransliterator` before synthesis,
  and written to the process as UTF-8.
- When either file is missing, the synthesizer logs instructions and stays disabled.
