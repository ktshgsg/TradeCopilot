using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradeCopilot.Core.Capture;

namespace TradeCopilot.Core.Vision;

/// <summary>
/// Runs Moondream2 vision Q&amp;A in a separate Python worker process and talks to it over
/// newline-delimited JSON on stdin/stdout.
/// </summary>
/// <remarks>
/// Python discovery is PATH-first with a fallback to
/// <c>%LOCALAPPDATA%\Programs\Python\Python3*\python.exe</c>. The worker script is copied
/// next to the binaries under <c>AppContext.BaseDirectory/tools/vision</c>. Everything is
/// fault-tolerant: a missing worker, interpreter, or model — an incompatible client, a hung
/// request, or a crashed process — all just leave <see cref="Enabled"/> false (or return an
/// empty answer) and are logged. Requests are serialized behind a single-permit semaphore
/// because the worker is one stdin/stdout pipe, and every wait is bounded so the
/// orchestrator can fall back to the LLM.
/// </remarks>
public sealed class MoondreamVisionEngine : IVisionEngine, IDisposable
{
    /// <summary>Total time allowed for the worker to report <c>ready</c>.</summary>
    public const int ReadyTimeoutMs = 90_000;

    /// <summary>Time allowed for a single describe request.</summary>
    public const int ReadTimeoutMs = 30_000;

    private readonly ILogger<MoondreamVisionEngine> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _processGate = new();

    private Process? _worker;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private bool _disposed;

    /// <summary>Creates the engine.</summary>
    /// <param name="log">Logger.</param>
    public MoondreamVisionEngine(ILogger<MoondreamVisionEngine> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <inheritdoc />
    public bool Enabled { get; private set; }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Enabled = false;

        string worker = Path.Combine(AppContext.BaseDirectory, "tools", "vision", "moondream_worker.py");
        if (!File.Exists(worker))
        {
            _log.LogWarning("Moondream worker not found at {Worker}; vision disabled.", worker);
            return;
        }

        if (!TryFindPython(out string python))
        {
            _log.LogWarning(
                "No Python interpreter found (PATH python.exe or %LOCALAPPDATA%\\Programs\\Python\\Python3*); vision disabled.");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = python,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add(worker);
            psi.Environment["PYTHONUNBUFFERED"] = "1";

            var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                _log.LogWarning("Failed to start the Moondream worker; vision disabled.");
                process.Dispose();
                return;
            }

            lock (_processGate)
            {
                _worker = process;
                _stdin = process.StandardInput;
                _stdout = process.StandardOutput;
            }

            DrainStdErr(process);

            bool ready = false;
            long deadline = ReadyTimeoutMs;
            var elapsed = Stopwatch.StartNew();
            while (elapsed.ElapsedMilliseconds < deadline)
            {
                int remaining = checked((int)(deadline - elapsed.ElapsedMilliseconds));
                string? line = await ReadLineAsync(remaining, ct).ConfigureAwait(false);
                if (line is null)
                {
                    _log.LogWarning("Timed out or the worker exited before reporting ready; vision disabled.");
                    break;
                }

                if (!TryParseStatus(line, out string? status, out string? message))
                {
                    continue; // ignore non-status chatter
                }

                if (status == "ready")
                {
                    ready = true;
                    break;
                }

                if (status == "error")
                {
                    _log.LogWarning("Moondream worker reported: {Message}; vision disabled.", message);
                    break;
                }
                // "loading" or anything else: keep waiting.
            }

            if (ready && !_disposed)
            {
                Enabled = true;
                _log.LogInformation(
                    "Moondream vision enabled using {Python} with worker {Worker}.", python, worker);
            }
            else
            {
                Enabled = false;
                KillWorker();
            }

            if (ct.IsCancellationRequested)
            {
                Enabled = false;
                KillWorker();
            }
        }
        catch (OperationCanceledException)
        {
            Enabled = false;
            KillWorker();
            _log.LogWarning("Moondream init cancelled; vision disabled.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Enabled = false;
            KillWorker();
            _log.LogWarning(ex, "Moondream init failed; vision disabled.");
        }
    }

    /// <inheritdoc />
    public async Task<string> DescribeAsync(FrameData frame, string prompt, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enabled || frame is null || string.IsNullOrWhiteSpace(prompt))
        {
            return string.Empty;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string image = Path.Combine(Path.GetTempPath(), $"tc-vision-{Guid.NewGuid():N}.png");
            try
            {
                SaveFrameAsPng(frame, image);

                Process? worker;
                StreamWriter? stdin;
                StreamReader? stdout;
                lock (_processGate)
                {
                    worker = _worker;
                    stdin = _stdin;
                    stdout = _stdout;
                }

                if (worker is null || worker.HasExited || stdin is null || stdout is null)
                {
                    _log.LogWarning("Moondream worker is not running; vision disabled.");
                    Enabled = false;
                    KillWorker();
                    return string.Empty;
                }

                string request = JsonSerializer.Serialize(new { image, prompt });
                await stdin.WriteLineAsync(request).ConfigureAwait(false);
                await stdin.FlushAsync(ct).ConfigureAwait(false);

                string? line = await ReadLineAsync(ReadTimeoutMs, ct).ConfigureAwait(false);
                if (line is null)
                {
                    _log.LogWarning(
                        "Moondream worker did not answer within {Timeout}s; vision disabled.",
                        ReadTimeoutMs / 1000);
                    Enabled = false;
                    KillWorker();
                    return string.Empty;
                }

                return ParseAnswer(line);
            }
            finally
            {
                TryDelete(image);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogWarning(ex, "Moondream describe failed; returning an empty answer.");
            return string.Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Kills the worker process and releases the semaphore.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Enabled = false;
        KillWorker();
        _gate.Dispose();
    }

    /// <summary>
    /// Finds a Python interpreter: PATH-first <c>python.exe</c>, then the default
    /// <c>%LOCALAPPDATA%\Programs\Python\Python3*</c> install locations.
    /// </summary>
    internal static bool TryFindPython(out string python)
    {
        python = string.Empty;

        // PATH-first. The WindowsApps entry is a Store alias stub (not a real interpreter),
        // so skip it to avoid launching the Store instead of running the worker.
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVar))
        {
            foreach (string entry in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string dir = entry.Trim().Trim('"');
                if (dir.Length == 0 || dir.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string candidate = Path.Combine(dir, "python.exe");
                if (File.Exists(candidate))
                {
                    python = candidate;
                    return true;
                }
            }
        }

        string programs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Python");
        if (Directory.Exists(programs))
        {
            foreach (string dir in Directory.GetDirectories(programs, "Python3*", SearchOption.TopDirectoryOnly)
                         .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string candidate = Path.Combine(dir, "python.exe");
                if (File.Exists(candidate))
                {
                    python = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private async Task<string?> ReadLineAsync(int timeoutMs, CancellationToken ct)
    {
        StreamReader? stdout;
        lock (_processGate)
        {
            stdout = _stdout;
        }

        if (stdout is null)
        {
            return null;
        }

        Task<string?> readTask = stdout.ReadLineAsync(ct).AsTask();
        Task timeoutTask = Task.Delay(timeoutMs, ct);

        Task finished = await Task.WhenAny(readTask, timeoutTask).ConfigureAwait(false);

        // Observe the loser so no cancelled/faulted task goes unobserved.
        _ = readTask.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        _ = timeoutTask.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

        return finished == readTask ? await readTask.ConfigureAwait(false) : null;
    }

    private void DrainStdErr(Process process)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (!process.HasExited)
                {
                    string? line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    _log.LogDebug("moondream: {Line}", line);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Stream closed as the worker exits; nothing to do.
                _log.LogDebug(ex, "Moondream stderr drain ended.");
            }
        });
    }

    private static void SaveFrameAsPng(FrameData frame, string path)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels is null)
        {
            throw new ArgumentException("Frame has invalid dimensions.", nameof(frame));
        }

        if (frame.Stride < frame.Width * 4)
        {
            throw new ArgumentException("Frame stride is too small for 32bpp pixels.", nameof(frame));
        }

        GCHandle handle = GCHandle.Alloc(frame.Pixels, GCHandleType.Pinned);
        try
        {
            IntPtr ptr = handle.AddrOfPinnedObject();
            using var bmp = new Bitmap(
                frame.Width, frame.Height, frame.Stride, PixelFormat.Format32bppArgb, ptr);
            bmp.Save(path, ImageFormat.Png);
        }
        finally
        {
            handle.Free();
        }
    }

    private static bool TryParseStatus(string line, out string? status, out string? message)
    {
        status = null;
        message = null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("status", out JsonElement statusElement))
            {
                return false;
            }

            status = statusElement.GetString();
            if (doc.RootElement.TryGetProperty("message", out JsonElement messageElement))
            {
                message = messageElement.GetString();
            }

            return status is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private string ParseAnswer(string line)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _log.LogWarning("Unexpected Moondream response: {Line}", line);
                return string.Empty;
            }

            if (root.TryGetProperty("error", out JsonElement error))
            {
                _log.LogWarning("Moondream worker error: {Error}", error.GetString());
                return string.Empty;
            }

            if (root.TryGetProperty("answer", out JsonElement answer))
            {
                return answer.GetString() ?? string.Empty;
            }

            _log.LogWarning("Moondream response had no answer: {Line}", line);
            return string.Empty;
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Unparseable Moondream response: {Line}", line);
            return string.Empty;
        }
    }

    private void KillWorker()
    {
        Process? process;
        lock (_processGate)
        {
            process = _worker;
            _worker = null;
            _stdin = null;
            _stdout = null;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogWarning(ex, "Error killing the Moondream worker.");
        }
        finally
        {
            process.Dispose();
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogDebug(ex, "Could not delete temporary frame {Path}.", path);
        }
    }
}



