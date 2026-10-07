using System.IO.Pipes;
using System.Text;
using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Orchestrator;
using TradeCopilot.Core.Privacy;
using TradeCopilot.Core.Speech;
using TradeCopilot.Service.Protocol;

namespace TradeCopilot.Service;

/// <summary>
/// Named-pipe JSON-lines server exposing the orchestrator to the UI.
/// </summary>
/// <remarks>
/// Each connection handles many newline-delimited JSON messages. Message-mode framing is
/// deliberately not relied upon: requests and responses are plain lines, so a client only
/// needs a <see cref="StreamReader"/>/<see cref="StreamWriter"/> pair. A failing request is
/// answered with an error line and never terminates the accept loop.
/// </remarks>
public sealed class PipeServer : BackgroundService
{
    /// <summary>Version reported by the "status" response.</summary>
    public const string ServiceVersion = "0.1.0";

    private const int MaxServerInstances = 4;
    private const int MaxLineLength = 1 << 20;

    private readonly AgentOrchestrator _orch;
    private readonly AgentState _state;
    private readonly IScreenCapture _capture;
    private readonly ISpeechRecognizer _asr;
    private readonly AgentOptions _opts;
    private readonly PrivacyGateCounters? _counters;
    private readonly ILogger<PipeServer> _log;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private int _connectionSeq;

    /// <summary>Creates the pipe server.</summary>
    /// <param name="orch">Query orchestrator.</param>
    /// <param name="state">Current chart state.</param>
    /// <param name="capture">Screen capture, for status and pause/resume.</param>
    /// <param name="asr">Speech recognizer, for the transcribe command.</param>
    /// <param name="opts">Service options.</param>
    /// <param name="counters">Privacy exclusion counters (optional).</param>
    /// <param name="log">Logger.</param>
    public PipeServer(
        AgentOrchestrator orch,
        AgentState state,
        IScreenCapture capture,
        ISpeechRecognizer asr,
        AgentOptions opts,
        ILogger<PipeServer> log,
        PrivacyGateCounters? counters = null)
    {
        ArgumentNullException.ThrowIfNull(orch);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(asr);
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(log);
        _orch = orch;
        _state = state;
        _capture = capture;
        _asr = asr;
        _opts = opts;
        _counters = counters;
        _log = log;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Pipe server listening on {PipeName}.", _opts.Pipe.Name);
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _opts.Pipe.Name,
                    PipeDirection.InOut,
                    MaxServerInstances,
                    PipeTransmissionMode.Byte,
                    System.IO.Pipes.PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                await HandleConnectionAsync(server, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.LogError(ex, "Pipe accept loop failed; retrying.");
                await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                if (server is not null)
                {
                    await server.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        _log.LogInformation("Pipe server stopped.");
    }

    /// <summary>Reads request lines and writes one response line per request.</summary>
    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        int connectionId = Interlocked.Increment(ref _connectionSeq);
        _log.LogInformation("Pipe connection {ConnectionId} opened.", connectionId);
        try
        {
            using var reader = new StreamReader(server, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(server, new UTF8Encoding(false), 4096, leaveOpen: true)
            {
                AutoFlush = false,
                NewLine = "\n",
            };

            while (!ct.IsCancellationRequested && server.IsConnected)
            {
                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                PipeResponse response;
                if (line.Length > MaxLineLength)
                {
                    response = new PipeResponse { Type = "error", Message = "request line too long" };
                }
                else
                {
                    PipeMessage? request = PipeMessage.Parse(line);
                    response = request is null
                        ? new PipeResponse { Type = "error", Message = "invalid JSON request" }
                        : await DispatchAsync(request, connectionId, ct).ConfigureAwait(false);
                }

                await writer.WriteLineAsync(response.ToJson().AsMemory(), ct).ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down: the loop caller disposes the pipe.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogError(ex, "Pipe connection {ConnectionId} failed.", connectionId);
        }
        finally
        {
            _log.LogInformation("Pipe connection {ConnectionId} closed.", connectionId);
        }
    }

    private async Task<PipeResponse> DispatchAsync(PipeMessage request, int connectionId, CancellationToken ct)
    {
        try
        {
            return request.Type switch
            {
                "query" => await AnswerQueryAsync(request, ct).ConfigureAwait(false),
                "status" => BuildStatus(),
                "pause_capture" => await PauseCaptureAsync(ct).ConfigureAwait(false),
                "resume_capture" => await ResumeCaptureAsync(ct).ConfigureAwait(false),
                "transcribe" => await TranscribeAsync(request, ct).ConfigureAwait(false),
                "panic" => await PanicAsync(ct).ConfigureAwait(false),
                _ => new PipeResponse { Type = "error", Message = $"unknown type '{request.Type}'" },
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogError(ex, "Pipe connection {ConnectionId}: '{Type}' failed.", connectionId, request.Type);
            return new PipeResponse { Type = "error", Message = $"'{request.Type}' failed: {ex.Message}" };
        }
    }
    private async Task<PipeResponse> AnswerQueryAsync(PipeMessage request, CancellationToken ct)
    {
        QueryResult result = await _orch.AnswerAsync(request.Text ?? string.Empty, ct).ConfigureAwait(false);
        return new PipeResponse
        {
            Type = "answer",
            Text = result.AnswerText,
            Language = result.LanguageCode,
            Source = result.Source,
            ElapsedMs = result.ElapsedMs,
        };
    }

    private PipeResponse BuildStatus()
    {
        AgentSnapshot snapshot = _state.Snapshot();
        long uptime = (long)(DateTimeOffset.UtcNow - _startedAt).TotalSeconds;
        return new PipeResponse
        {
            Type = "status",
            CaptureActive = _capture.IsCapturing,
            FramesProcessed = snapshot.FramesProcessed,
            FramesExcludedByWindow = _counters?.ExcludedByWindow,
            FramesExcludedBySensitive = _counters?.ExcludedBySensitive,
            UptimeSec = uptime,
            Version = ServiceVersion,
        };
    }

    private async Task<PipeResponse> PauseCaptureAsync(CancellationToken ct)
    {
        await _capture.StopAsync(ct).ConfigureAwait(false);
        _log.LogInformation("Capture paused via pipe.");
        return new PipeResponse { Type = "ok" };
    }

    private async Task<PipeResponse> ResumeCaptureAsync(CancellationToken ct)
    {
        await _capture.StartAsync(_opts.Capture.Region(), _opts.Capture.Fps, ct).ConfigureAwait(false);
        _log.LogInformation("Capture resumed via pipe.");
        return new PipeResponse { Type = "ok" };
    }

    private async Task<PipeResponse> TranscribeAsync(PipeMessage request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.AudioBase64))
        {
            return new PipeResponse { Type = "error", Message = "transcribe requires audioBase64" };
        }

        byte[] pcm;
        try
        {
            pcm = Convert.FromBase64String(request.AudioBase64);
        }
        catch (FormatException)
        {
            return new PipeResponse { Type = "error", Message = "audioBase64 is not valid base64" };
        }

        string transcript = await _asr.TranscribeAsync(pcm, ct).ConfigureAwait(false);
        return new PipeResponse { Type = "answer", Text = transcript, Source = "asr" };
    }

    /// <summary>
    /// Emergency reset: stops capture and clears all agent state.
    /// </summary>
    /// <remarks>
    /// <see cref="ISpeechRecognizer"/> has no stop operation — each
    /// <c>TranscribeAsync</c> call is self-contained and guarded by its own semaphore —
    /// so there is nothing to cancel here beyond stopping capture and dropping state.
    /// </remarks>
    private async Task<PipeResponse> PanicAsync(CancellationToken ct)
    {
        await _capture.StopAsync(ct).ConfigureAwait(false);
        _state.Clear();
        _log.LogWarning("Panic: capture stopped and agent state cleared.");
        return new PipeResponse { Type = "ok" };
    }
}
