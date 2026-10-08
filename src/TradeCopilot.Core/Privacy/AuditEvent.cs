namespace TradeCopilot.Core.Privacy;

/// <summary>Content-free audit events written to <c>data/audit.log</c>.</summary>
public enum AuditEvent
{
    /// <summary>Screen capture started.</summary>
    CaptureStarted,

    /// <summary>Screen capture paused.</summary>
    CapturePaused,

    /// <summary>A query was answered; meta carries source and latency only.</summary>
    AnswerSource,

    /// <summary>A frame was dropped by a privacy gate; meta carries the reason tag.</summary>
    SensitiveSkipped,

    /// <summary>Emergency reset was triggered via the pipe.</summary>
    Panic,
}
