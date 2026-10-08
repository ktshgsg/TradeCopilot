using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TradeCopilot.Core.Privacy;

/// <summary>
/// Append-only, content-free audit log. Each line is one JSON object with
/// <c>ts</c>, <c>evt</c>, and <c>meta</c> fields.
/// </summary>
public sealed class AuditLog : IAuditSink, IAsyncDisposable
{
    private const int MaxMetaLength = 512;

    private static bool _warned;

    private readonly string _path;
    private readonly ILogger<AuditLog> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FileStream? _stream;
    private bool _disposed;

    /// <summary>Creates the audit log, creating the parent directory if needed.</summary>
    /// <param name="path">Log file path.</param>
    /// <param name="log">Logger.</param>
    public AuditLog(string path, ILogger<AuditLog> log)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(log);
        _path = path;
        _log = log;

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
    }

    /// <summary>
    /// Records one audit event. <paramref name="meta"/> must be a short
    /// reason/label. NEVER pass raw OCR text, query text, or window titles.
    /// Use hashes or short tags.
    /// </summary>
    /// <param name="eventName">Event kind, e.g. "SensitiveSkipped".</param>
    /// <param name="meta">Short label only — never raw content.</param>
    public void Write(string eventName, string meta)
    {
        if (_disposed)
        {
            WarnOnce("Audit log is disposed; dropping event.");
            return;
        }

        string trimmed = meta.Length > MaxMetaLength ? meta[..MaxMetaLength] : meta;
        string line = JsonSerializer.Serialize(new
        {
            ts = DateTimeOffset.UtcNow,
            evt = eventName,
            meta = trimmed,
        });

        _gate.Wait();
        try
        {
            if (_disposed || _stream is null)
            {
                return;
            }

            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(line + "\n");
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            WarnOnce($"Audit write failed: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stream?.Dispose();
            _stream = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private void WarnOnce(string message)
    {
        if (Volatile.Read(ref _warned))
        {
            return;
        }

        Volatile.Write(ref _warned, true);
        _log.LogWarning("{Message}", message);
    }
}
