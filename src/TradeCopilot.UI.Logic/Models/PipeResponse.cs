using System.Text.Json;

namespace TradeCopilot.UI.Logic.Models;

/// <summary>One response line received from the service over the named pipe.</summary>
public sealed record PipeResponse
{
    /// <summary>Serializer settings for pipe reads. Property names match case-insensitively.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Response type: "answer", "status", "ok", or "error".</summary>
    public string Type { get; init; } = "error";

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

    /// <summary>Frames dropped before OCR because the window title was excluded.</summary>
    public long? FramesExcludedByWindow { get; init; }

    /// <summary>Frames dropped after OCR because the content was sensitive.</summary>
    public long? FramesExcludedBySensitive { get; init; }

    /// <summary>Seconds since the service started.</summary>
    public long? UptimeSec { get; init; }

    /// <summary>Service version.</summary>
    public string? Version { get; init; }

    /// <summary>Human-readable error text.</summary>
    public string? Message { get; init; }

    /// <summary>Parses one response line. Never throws: failures become an "error" response.</summary>
    /// <param name="line">JSON text.</param>
    /// <returns>The parsed response, or an error response when the line is not valid JSON.</returns>
    public static PipeResponse Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return new PipeResponse { Type = "error", Message = "Empty response from service." };
        }

        try
        {
            return JsonSerializer.Deserialize<PipeResponse>(line, JsonOptions)
                ?? new PipeResponse { Type = "error", Message = "Empty response from service." };
        }
        catch (JsonException ex)
        {
            return new PipeResponse { Type = "error", Message = $"Invalid response from service: {ex.Message}" };
        }
    }
}
