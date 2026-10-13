namespace TradeCopilot.Core.Privacy;

/// <summary>
/// No-op <see cref="IAuditSink"/> used until the full audit log lands.
/// </summary>
public sealed class NoopAuditSink : IAuditSink
{
    /// <summary>Shared instance registered in DI and tests.</summary>
    public static NoopAuditSink Instance { get; } = new();

    private NoopAuditSink()
    {
    }

    /// <inheritdoc />
    public void Write(string eventName, string meta)
    {
    }
}
