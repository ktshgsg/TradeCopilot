using TradeCopilot.Core.Capture;

namespace TradeCopilot.Service;

/// <summary>Typed root of <c>appsettings.json</c>.</summary>
public sealed class AgentOptions
{
    /// <summary>Screen-capture region and rate.</summary>
    public CaptureOptions Capture { get; set; } = new();

    /// <summary>Model file locations.</summary>
    public ModelOptions Models { get; set; } = new();

    /// <summary>Semantic-cache settings.</summary>
    public CacheOptions Cache { get; set; } = new();

    /// <summary>Named-pipe settings.</summary>
    public PipeOptions Pipe { get; set; } = new();
}

/// <summary>Desktop region and frame rate to capture.</summary>
public sealed class CaptureOptions
{
    /// <summary>Region left edge in desktop pixels.</summary>
    public int X { get; set; }

    /// <summary>Region top edge in desktop pixels.</summary>
    public int Y { get; set; }

    /// <summary>Region width; 0 captures the full output width.</summary>
    public int Width { get; set; }

    /// <summary>Region height; 0 captures the full output height.</summary>
    public int Height { get; set; }

    /// <summary>Frame-rate cap. 2 keeps OCR load light while a chart is on screen.</summary>
    public int Fps { get; set; } = 2;

    /// <summary>Converts these settings to a <see cref="CaptureRegion"/>.</summary>
    /// <returns>The configured capture region.</returns>
    public CaptureRegion Region() => new(X, Y, Width, Height);
}

/// <summary>Model file paths. All are relative to the service working directory.</summary>
public sealed class ModelOptions
{
    /// <summary>Sentence-embedding ONNX model.</summary>
    public string EmbeddingPath { get; set; } = "models/embedding_model.onnx";

    /// <summary>GGUF chat model for <c>LlamaSharp</c>.</summary>
    public string LlmPath { get; set; } = "models/qwen2.5-3b-instruct-q4_k_m.gguf";

    /// <summary>GGML model for Whisper ASR.</summary>
    public string WhisperPath { get; set; } = "models/ggml-medium.bin";

    /// <summary>Piper executable for text-to-speech.</summary>
    public string PiperExePath { get; set; } = "tools/piper/piper.exe";

    /// <summary>Piper ONNX voice model.</summary>
    public string PiperVoicePath { get; set; } = "models/piper/hi_IN-rohan-medium.onnx";
}

/// <summary>Semantic-cache database and retention settings.</summary>
public sealed class CacheOptions
{
    /// <summary>SQLite database path.</summary>
    public string DbPath { get; set; } = "data/cache.db";

    /// <summary>Minimum cosine similarity for a cache hit.</summary>
    public float SimilarityThreshold { get; set; } = 0.88f;

    /// <summary>Entries older than this are purged on startup.</summary>
    public int MaxAgeDays { get; set; } = 7;
}

/// <summary>Named-pipe endpoint settings.</summary>
public sealed class PipeOptions
{
    /// <summary>Pipe name the UI connects to.</summary>
    public string Name { get; set; } = "TradeCopilot.Pipe";
}
