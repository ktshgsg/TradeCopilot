namespace TradeCopilot.UI.Logic.ViewModels;

/// <summary>Which side of the conversation a chat bubble belongs to.</summary>
public enum MessageRole
{
    /// <summary>A message typed by the user.</summary>
    User,

    /// <summary>A message returned by the service.</summary>
    Assistant,

    /// <summary>A centered notice rendered by the UI itself.</summary>
    System,
}
