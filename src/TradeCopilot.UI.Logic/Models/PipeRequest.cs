using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradeCopilot.UI.Logic.Models;

/// <summary>One request line sent to the service over the named pipe.</summary>
/// <param name="Type">Request type: "query", "status", "pause_capture", "resume_capture", "transcribe", "purge_cache", or "panic".</param>
/// <param name="Text">Query text for "query" requests.</param>
/// <param name="AudioBase64">Base64 PCM16 audio for "transcribe" requests.</param>
public sealed record PipeRequest(
    string Type,
    string? Text = null,
    string? AudioBase64 = null)
{
    /// <summary>Serializer settings for pipe traffic. Null members are omitted.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serializes this request to a single JSON line (without the newline).</summary>
    /// <returns>JSON text.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
