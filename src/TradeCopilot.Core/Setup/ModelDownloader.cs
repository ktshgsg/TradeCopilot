using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace TradeCopilot.Core.Setup;

/// <summary>Progress of one in-flight model download.</summary>
/// <param name="BytesReceived">Bytes on disk so far, including any resumed prefix.</param>
/// <param name="TotalBytes">Total expected size; 0 when the server reported nothing and the manifest has no estimate.</param>
/// <param name="BytesPerSecond">Average throughput of the current attempt.</param>
public sealed record DownloadProgress(long BytesReceived, long TotalBytes, double BytesPerSecond);

/// <summary>
/// Downloads one <see cref="ModelFile"/> with a resumable <c>.partial</c> sidecar and
/// optional SHA-256 verification. Responses are streamed (never buffered), so a 1 GB model
/// never lands in memory; progress is throttled to one report per 250 ms.
/// </summary>
/// <remarks>
/// The <see cref="HttpClient"/> must be dedicated to downloads and must not have sent a
/// request yet: the constructor configures it (infinite timeout, "TradeCopilot/0.2" user
/// agent). Cancellation and failures leave the <c>.partial</c> file in place so the next
/// attempt resumes with a Range request.
/// </remarks>
public sealed class ModelDownloader
{
    private const string UserAgent = "TradeCopilot/0.2";
    private const int BufferSize = 81920;
    private const int ProgressIntervalMs = 250;

    private readonly HttpClient _http;
    private readonly ILogger<ModelDownloader> _log;

    /// <summary>Creates a downloader.</summary>
    /// <param name="http">Dedicated HTTP client; configured here (timeout, user agent).</param>
    /// <param name="log">Logger.</param>
    public ModelDownloader(HttpClient http, ILogger<ModelDownloader> log)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(log);
        if (http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }

        if (http.Timeout != Timeout.InfiniteTimeSpan)
        {
            http.Timeout = Timeout.InfiniteTimeSpan;
        }

        _http = http;
        _log = log;
    }
    /// <summary>
    /// Downloads <paramref name="file"/> to its target path. When a <c>.partial</c> file
    /// exists the request resumes with a Range header; a server that answers 200 instead
    /// of 206 restarts the file from scratch. A SHA-256 mismatch deletes the partial file
    /// and throws <see cref="InvalidDataException"/>.
    /// </summary>
    /// <param name="file">File to download (absolute or relative target path).</param>
    /// <param name="progress">Progress sink; reports at most every 250 ms plus one final update.</param>
    /// <param name="ct">Cancellation token; cancel leaves the partial file in place.</param>
    /// <returns>Completed work.</returns>
    public async Task DownloadAsync(ModelFile file, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(progress);
        ct.ThrowIfCancellationRequested();

        string fullTarget = Path.GetFullPath(file.TargetPath);
        string? directory = Path.GetDirectoryName(fullTarget);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string partialPath = fullTarget + ".partial";
        if (File.Exists(fullTarget))
        {
            long presentLength = new FileInfo(fullTarget).Length;
            progress.Report(new DownloadProgress(presentLength, presentLength, 0));
            _log.LogDebug("{Name} already present at {Path}; skipping download.", file.Name, fullTarget);
            return;
        }

        bool verify = !string.IsNullOrEmpty(file.Sha256);
        byte[]? hash = null;

        var (response, startOffset) = await SendWithResumeAsync(file, partialPath, ct).ConfigureAwait(false);
        using (response)
        {
            response.EnsureSuccessStatusCode();
            bool append = startOffset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (startOffset > 0 && !append)
            {
                // The server ignored the Range header; restart the file from scratch.
                startOffset = 0;
            }

            long totalBytes = ResolveTotalBytes(response, startOffset, append, file);
            IncrementalHash? sha = verify ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
            try
            {
                await using (var fs = new FileStream(
                    partialPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, BufferSize, useAsync: true))
                {
                    if (!append)
                    {
                        fs.SetLength(0);
                    }

                    if (append && verify && fs.Length > 0)
                    {
                        await SeedHashAsync(fs, sha!, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        fs.Position = append ? fs.Length : 0;
                    }

                    await using Stream source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    var buffer = new byte[BufferSize];
                    long sessionBytes = 0;
                    var stopwatch = Stopwatch.StartNew();
                    long lastReportMs = -ProgressIntervalMs;
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        sha?.AppendData(buffer.AsSpan(0, read));
                        sessionBytes += read;

                        long elapsedMs = stopwatch.ElapsedMilliseconds;
                        if (elapsedMs - lastReportMs >= ProgressIntervalMs)
                        {
                            lastReportMs = elapsedMs;
                            double bytesPerSecond = elapsedMs > 0 ? sessionBytes * 1000.0 / elapsedMs : 0;
                            progress.Report(new DownloadProgress(startOffset + sessionBytes, totalBytes, bytesPerSecond));
                        }
                    }

                    stopwatch.Stop();
                    double finalBps = stopwatch.Elapsed.TotalSeconds > 0 ? sessionBytes / stopwatch.Elapsed.TotalSeconds : 0;
                    long finalReceived = startOffset + sessionBytes;
                    progress.Report(new DownloadProgress(
                        finalReceived, totalBytes > 0 ? totalBytes : finalReceived, finalBps));

                    if (sha is not null)
                    {
                        hash = sha.GetHashAndReset();
                    }
                }
            }
            finally
            {
                sha?.Dispose();
            }
        }

        if (hash is not null)
        {
            string actual = Convert.ToHexString(hash);
            if (!actual.Equals(file.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partialPath);
                _log.LogError(
                    "SHA256 mismatch for {Name}: expected {Expected}, got {Actual}; deleted {Partial}.",
                    file.Name, file.Sha256, actual, partialPath);
                throw new InvalidDataException($"SHA256 mismatch for {file.Name}: expected {file.Sha256}, got {actual}.");
            }
        }

        File.Move(partialPath, fullTarget, overwrite: true);
        _log.LogInformation("Downloaded {Name} ({Bytes} bytes) to {Path}.", file.Name, new FileInfo(fullTarget).Length, fullTarget);
    }

    /// <summary>
    /// Sends the GET, adding a Range header when a partial file exists. A 416 for an
    /// existing partial (e.g. the file finished but the rename never happened) deletes the
    /// partial and retries once without the Range header.
    /// </summary>
    /// <param name="file">File being downloaded.</param>
    /// <param name="partialPath">Path of the partial sidecar.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The response plus the partial length the download should resume from.</returns>
    private async Task<(HttpResponseMessage Response, long StartOffset)> SendWithResumeAsync(
        ModelFile file, string partialPath, CancellationToken ct)
    {
        long existing = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, file.Url);
            if (existing > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existing, null);
            }

            HttpResponseMessage response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (existing > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // The partial file is unusable; delete it and retry once from scratch.
                response.Dispose();
                File.Delete(partialPath);
                existing = 0;
                continue;
            }

            return (response, existing);
        }
    }

    /// <summary>Feeds the bytes of an existing partial file into the hash before appending.</summary>
    /// <param name="fs">Open stream over the partial file (left positioned at the end).</param>
    /// <param name="sha">Hash to seed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Completed work.</returns>
    private static async Task SeedHashAsync(FileStream fs, IncrementalHash sha, CancellationToken ct)
    {
        fs.Position = 0;
        var buffer = new byte[BufferSize];
        int read;
        while ((read = await fs.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            sha.AppendData(buffer.AsSpan(0, read));
        }
    }

    /// <summary>
    /// Resolves the total byte count for progress reporting: the content range total when
    /// resuming, otherwise the content length, then the manifest estimate, else 0.
    /// </summary>
    /// <param name="response">Server response.</param>
    /// <param name="startOffset">Resumed prefix length (0 for a fresh download).</param>
    /// <param name="append">Whether the response is a 206 append.</param>
    /// <param name="file">Manifest entry with the fallback estimate.</param>
    /// <returns>Total bytes, or 0 when unknown.</returns>
    private static long ResolveTotalBytes(HttpResponseMessage response, long startOffset, bool append, ModelFile file)
    {
        if (append)
        {
            if (response.Content.Headers.ContentRange?.Length is { } rangeTotal && rangeTotal > 0)
            {
                return rangeTotal;
            }

            if (response.Content.Headers.ContentLength is long remaining and > 0)
            {
                return startOffset + remaining;
            }
        }
        else if (response.Content.Headers.ContentLength is long full and > 0)
        {
            return full;
        }

        return file.ExpectedSize > 0 ? file.ExpectedSize : 0;
    }
}