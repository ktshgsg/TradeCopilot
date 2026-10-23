using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging;
using TradeCopilot.UI.Logic.Models;

namespace TradeCopilot.UI.Logic.Services;

/// <summary>
/// Named-pipe JSON-lines client. Opens a fresh connection per request and uses raw byte
/// I/O: never wraps the same <see cref="NamedPipeClientStream"/> in both a reader and a
/// writer, which would dispose the stream.
/// </summary>
public sealed class PipeClient : IPipeClient, IAsyncDisposable
{
    private const int ConnectTimeoutMs = 15000;
    private const int ConnectRetryDelayMs = 1000;
    private const int RetryDelayMs = 200;
    private const int MaxLineBytes = 1 << 20;

    private readonly UiOptions _opts;
    private readonly ILogger<PipeClient> _log;
    private bool _disposed;

    /// <summary>Creates the pipe client.</summary>
    /// <param name="opts">UI options. Passed directly; there is no options pipeline in the plain WPF app.</param>
    /// <param name="log">Logger. The UI passes <c>NullLogger&lt;PipeClient&gt;.Instance</c>.</param>
    public PipeClient(UiOptions opts, ILogger<PipeClient> log)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(log);
        _opts = opts;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<PipeResponse> SendAsync(PipeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_disposed)
        {
            return new PipeResponse { Type = "error", Message = "Pipe client is disposed." };
        }

        try
        {
            return await SendOnceAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            _log.LogWarning(ex, "Pipe request '{Type}' failed; retrying once.", request.Type);
            try
            {
                await Task.Delay(RetryDelayMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new PipeResponse { Type = "error", Message = "Request cancelled." };
            }

            try
            {
                return await SendOnceAsync(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new PipeResponse { Type = "error", Message = "Request cancelled." };
            }
            catch (Exception retryEx) when (retryEx is not OutOfMemoryException)
            {
                _log.LogError(retryEx, "Pipe request '{Type}' failed after retry.", request.Type);
                return new PipeResponse { Type = "error", Message = $"Service unreachable: {retryEx.Message}" };
            }
        }
        catch (OperationCanceledException)
        {
            return new PipeResponse { Type = "error", Message = "Request cancelled." };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogError(ex, "Pipe request '{Type}' failed.", request.Type);
            return new PipeResponse { Type = "error", Message = $"Service unreachable: {ex.Message}" };
        }
    }

    /// <summary>Sends one request over a fresh pipe connection and reads one response line.</summary>
    /// <param name="request">Request to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parsed service response.</returns>
    private async Task<PipeResponse> SendOnceAsync(PipeRequest request, CancellationToken ct)
    {
        using var stream = new NamedPipeClientStream(".", _opts.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await ConnectWithRetryAsync(stream, ConnectTimeoutMs, ConnectRetryDelayMs, ct).ConfigureAwait(false);

        byte[] payload = Encoding.UTF8.GetBytes(request.ToJson() + "\n");
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        string line = await ReadLineAsync(stream, ct).ConfigureAwait(false);
        return PipeResponse.Parse(line);
    }

    /// <summary>
    /// Connects to the named pipe, retrying once after a 1-second delay. The
    /// service needs ~11 seconds to load the LLM and may not accept pipe
    /// connections immediately after startup.
    /// </summary>
    private static async Task ConnectWithRetryAsync(NamedPipeClientStream stream, int connectTimeoutMs, int connectRetryDelayMs, CancellationToken ct)
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await stream.ConnectAsync(connectTimeoutMs, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException)
            {
                lastError = ex;
                if (attempt == 0)
                {
                    try
                    {
                        await Task.Delay(connectRetryDelayMs, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    continue;
                }
                throw;
            }
        }
        throw lastError ?? new IOException("Unable to connect to the named pipe.");
    }

    /// <summary>Reads raw bytes until '\n' or the size cap, then decodes as UTF-8.</summary>
    /// <param name="stream">Connected pipe stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The decoded line without the trailing newline.</returns>
    private static async Task<string> ReadLineAsync(NamedPipeClientStream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (buffer.Length < MaxLineBytes)
        {
            int read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            int newline = Array.IndexOf(chunk, (byte)'\n', 0, read);
            if (newline >= 0)
            {
                buffer.Write(chunk, 0, newline);
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
