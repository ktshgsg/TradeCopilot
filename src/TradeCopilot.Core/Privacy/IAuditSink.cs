namespace TradeCopilot.Core.Privacy;

/// <summary>
/// Minimal audit sink consumed by the privacy gates. The full
/// <c>AuditLog</c> will implement this interface; using it now means the
/// gate code does not change later.
/// </summary>
public interface IAuditSink
{
    /// <summary>Records one content-free audit event.</summary>
    /// <param name="eventName">Event kind, e.g. "SensitiveSkipped".</param>
    /// <param name="meta">Short label only — never raw content.</param>
    void Write(string eventName, string meta);
}
