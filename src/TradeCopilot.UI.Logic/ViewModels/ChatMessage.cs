namespace TradeCopilot.UI.Logic.ViewModels;

/// <summary>One chat bubble in the overlay conversation.</summary>
public sealed class ChatMessage
{
    /// <summary>Creates a chat message.</summary>
    /// <param name="role">Which side of the conversation it belongs to.</param>
    /// <param name="text">Message text.</param>
    /// <param name="source">Answer stage ("cache", "rule", "llm", "error"); only set for assistant messages.</param>
    /// <param name="elapsedMs">How long answering took; only set for assistant messages.</param>
    public ChatMessage(MessageRole role, string text, string? source = null, long? elapsedMs = null)
    {
        Role = role;
        Text = text;
        Source = source;
        ElapsedMs = elapsedMs;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>Which side of the conversation this message belongs to.</summary>
    public MessageRole Role { get; }

    /// <summary>Message text.</summary>
    public string Text { get; }

    /// <summary>Answer stage; only set for assistant messages.</summary>
    public string? Source { get; }

    /// <summary>How long answering took; only set for assistant messages.</summary>
    public long? ElapsedMs { get; }

    /// <summary>When the message was created.</summary>
    public DateTimeOffset Timestamp { get; }
}
