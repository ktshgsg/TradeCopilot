using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradeCopilot.Service.Protocol;

/// <summary>
/// One request or response line on the named pipe.
/// </summary>
/// <param name="Type">Message type, e.g. "query", "status", "answer".</param>
/// <param name="Text">Query or answer text.</param>
/// <param name="AudioBase64">Base64 PCM16 audio for "transcribe".</param>
/// <param name="Error">Error text for "error" responses.</param>
public sealed record PipeMessage(
    string Type,
    string? Text,
    string? AudioBase64,
    string? Error)
{
    /// <summary>Serializer settings shared by the pipe client and server.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Deserializes one request line.</summary>
    /// <param name="line">JSON text.</param>
    /// <returns>The parsed message, or null when the line is not valid JSON.</returns>
    public static PipeMessage? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PipeMessage>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Serializes this message to a single JSON line (without the newline).</summary>
    /// <returns>JSON text.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

/// <summary>
/// One response line. Every field except <see cref="Type"/> is optional and omitted when
/// null, so each response type only carries the members it needs.
/// </summary>
public sealed record PipeResponse
{
    /// <summary>Response type: "answer", "status", "ok", or "error".</summary>
    public string Type { get; init; } = "ok";

    /// <summary>Answer or transcript text.</summary>
    public string? Text { get; init; }

    /// <summary>Answer language code.</summary>
    public string? Language { get; init; }

    /// <summary>Which stage answered: "cache", "rule", "llm", "asr", or "empty".</summary>
    public string? Source { get; init; }

    /// <summary>How long answering took.</summary>
    public long? ElapsedMs { get; init; }

    /// <summary>Whether screen capture is running.</summary>
    public bool? CaptureActive { get; init; }

    /// <summary>Frames folded into the agent state.</summary>
    public long? FramesProcessed { get; init; }

    /// <summary>Seconds since the service started.</summary>
    public long? UptimeSec { get; init; }

    /// <summary>Service version.</summary>
    public string? Version { get; init; }

    /// <summary>Human-readable error text.</summary>
    public string? Message { get; init; }

    /// <summary>Serializes this response to a single JSON line (without the newline).</summary>
    /// <returns>JSON text.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, PipeMessage.JsonOptions);
}
