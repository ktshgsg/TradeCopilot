using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCopilot.Core.Setup;
using TradeCopilot.UI.Logic;
using TradeCopilot.UI.Logic.ViewModels;

namespace TradeCopilot.Tests;

/// <summary>
/// Tests for the first-run model wizard: manifest helpers, resumable downloads with
/// SHA-256 verification, the flag gate, and the view model (skip, completion, throttling).
/// </summary>
public sealed class FirstRunSetupTests : IDisposable
{
    private readonly List<string> _roots = [];

    /// <summary>Creates an isolated temp directory and registers it for cleanup.</summary>
    private string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TradeCopilot.FirstRunTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _roots.Add(dir);
        return dir;
    }

    /// <summary>Removes every temp directory created by this test class.</summary>
    public void Dispose()
    {
        foreach (string dir in _roots)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Counts requests and answers from a canned responder.</summary>
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
            _respond = respond;

        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            return _respond(request, cancellationToken);
        }
    }

    /// <summary>IProgress that invokes the handler inline (deterministic in tests).</summary>
    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public InlineProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
    }

    /// <summary>Byte source that trickles out in chunks with a per-chunk delay.</summary>
    private sealed class SlowStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunkSize;
        private readonly int _delayMs;
        private int _position;

        public SlowStream(byte[] data, int chunkSize, int delayMs)
        {
            _data = data;
            _chunkSize = chunkSize;
            _delayMs = delayMs;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(Math.Min(_chunkSize, count), _data.Length - _position);
            Array.Copy(_data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _data.Length)
            {
                return 0;
            }

            await Task.Delay(_delayMs, cancellationToken);
            int n = Math.Min(Math.Min(_chunkSize, buffer.Length), _data.Length - _position);
            _data.AsMemory(_position, n).CopyTo(buffer);
            _position += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>The manifest pins the four model files from the setup script.</summary>
    [Fact]
    public void ModelManifest_Required_HasFourFilesWithHuggingFaceUrls()
    {
        ModelManifest.Required.Should().HaveCount(4);
        ModelManifest.Optional.Should().BeEmpty();
        ModelManifest.Required.Should().OnlyContain(f =>
            f.Url.StartsWith("https://huggingface.co/", StringComparison.Ordinal) &&
            f.ExpectedSize > 0 &&
            string.IsNullOrEmpty(f.Sha256));
        ModelManifest.Required.Select(f => f.TargetPath).Should().Contain("models/qwen2.5-1.5b-instruct-q4_k_m.gguf");
        ModelManifest.Required.Select(f => f.TargetPath).Should().Contain("models/ggml-base.bin");
        ModelManifest.Required.Select(f => f.TargetPath).Should().Contain("models/piper/hi_IN-rohan-medium.onnx");
        ModelManifest.Required.Select(f => f.TargetPath).Should().Contain("models/piper/hi_IN-rohan-medium.onnx.json");
    }

    /// <summary>AllPresent flips true only once every required file exists.</summary>
    [Fact]
    public void ModelManifest_AllPresent_DetectsMissingThenComplete()
    {
        string dir = NewTempDir();

        ModelManifest.AllPresent(dir).Should().BeFalse();
        ModelManifest.Missing(dir).Should().HaveCount(4);

        foreach (ModelFile file in ModelManifest.Required)
        {
            string path = ModelManifest.ResolvePath(file, dir);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
        }

        ModelManifest.AllPresent(dir).Should().BeTrue();
        ModelManifest.Missing(dir).Should().BeEmpty();
    }

    /// <summary>Each required file maps to the engine it feeds.</summary>
    [Fact]
    public void ModelManifest_EngineName_MapsToLlmAsrOrTts()
    {
        ModelManifest.EngineName(ModelManifest.Required[0]).Should().Be("LLM");
        ModelManifest.EngineName(ModelManifest.Required[1]).Should().Be("ASR");
        ModelManifest.EngineName(ModelManifest.Required[2]).Should().Be("TTS");
        ModelManifest.EngineName(ModelManifest.Required[3]).Should().Be("TTS");
    }

    /// <summary>The flag gate shows only while the flag is missing and models are absent.</summary>
    [Fact]
    public void FirstRunGate_ShouldShow_UntilMarkCompleteOrModelsPresent()
    {
        string flag = Path.Combine(NewTempDir(), "sub", ".firstrun-complete");

        FirstRunGate.ShouldShow(flag, modelsPresent: false).Should().BeTrue();
        FirstRunGate.ShouldShow(flag, modelsPresent: true).Should().BeFalse();

        FirstRunGate.MarkComplete(flag);
        File.Exists(flag).Should().BeTrue();
        FirstRunGate.ShouldShow(flag, modelsPresent: false).Should().BeFalse();
    }

    /// <summary>A plain 200 download writes the target, removes the partial, and reports progress.</summary>
    [Fact]
    public async Task ModelDownloader_DownloadAsync_WritesFileAndReportsProgress()
    {
        string dir = NewTempDir();
        string target = Path.Combine(dir, "models", "file.bin");
        byte[] data = new byte[10_000];
        Random.Shared.NextBytes(data);
        var handler = new FakeHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) }));
        using var http = new HttpClient(handler);
        var downloader = new ModelDownloader(http, NullLogger<ModelDownloader>.Instance);
        var file = new ModelFile("file", "http://fake/file.bin", target, data.Length, string.Empty);

        var reports = new List<DownloadProgress>();
        await downloader.DownloadAsync(file, new InlineProgress<DownloadProgress>(reports.Add), CancellationToken.None);

        File.ReadAllBytes(target).Should().Equal(data);
        File.Exists(target + ".partial").Should().BeFalse();
        reports.Should().NotBeEmpty();
        reports[^1].BytesReceived.Should().Be(data.Length);
        reports[^1].TotalBytes.Should().Be(data.Length);
        http.DefaultRequestHeaders.UserAgent.ToString().Should().Be("TradeCopilot/0.2");
        http.Timeout.Should().Be(Timeout.InfiniteTimeSpan);
    }

    /// <summary>An already-present target short-circuits without a network request.</summary>
    [Fact]
    public async Task ModelDownloader_AlreadyPresent_SkipsRequest()
    {
        string dir = NewTempDir();
        string target = Path.Combine(dir, "file.bin");
        File.WriteAllBytes(target, new byte[10]);
        var handler = new FakeHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var http = new HttpClient(handler);
        var downloader = new ModelDownloader(http, NullLogger<ModelDownloader>.Instance);

        await downloader.DownloadAsync(
            new ModelFile("file", "http://fake/file.bin", target, 10, string.Empty),
            new InlineProgress<DownloadProgress>(_ => { }),
            CancellationToken.None);

        handler.Requests.Should().Be(0);
    }

    /// <summary>A matching SHA-256 passes; the file lands at the target.</summary>
    [Fact]
    public async Task ModelDownloader_HashMatch_Succeeds()
    {
        string dir = NewTempDir();
        string target = Path.Combine(dir, "file.bin");
        byte[] data = new byte[2_000];
        Random.Shared.NextBytes(data);
        string sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var handler = new FakeHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) }));
        using var http = new HttpClient(handler);
        var downloader = new ModelDownloader(http, NullLogger<ModelDownloader>.Instance);

        await downloader.DownloadAsync(
            new ModelFile("file", "http://fake/file.bin", target, data.Length, sha),
            new InlineProgress<DownloadProgress>(_ => { }),
            CancellationToken.None);

        File.ReadAllBytes(target).Should().Equal(data);
    }
    /// <summary>A partial file resumes with a Range request; the hash covers the whole file.</summary>
    [Fact]
    public async Task ModelDownloader_Resume_SendsRangeHeaderAndAppends()
    {
        string dir = NewTempDir();
        string target = Path.Combine(dir, "file.bin");
        byte[] data = new byte[1_000];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(target + ".partial", data[..400]);
        string sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        string? seenRange = null;
        var handler = new FakeHttpHandler((req, _) =>
        {
            seenRange = req.Headers.Range?.ToString();
            var content = new ByteArrayContent(data[400..]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(400, data.Length - 1, data.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
        });
        using var http = new HttpClient(handler);
        var downloader = new ModelDownloader(http, NullLogger<ModelDownloader>.Instance);

        await downloader.DownloadAsync(
            new ModelFile("file", "http://fake/file.bin", target, data.Length, sha),
            new InlineProgress<DownloadProgress>(_ => { }),
            CancellationToken.None);

        seenRange.Should().Be("bytes=400-");
        File.ReadAllBytes(target).Should().Equal(data);
        File.Exists(target + ".partial").Should().BeFalse();
    }

    /// <summary>A server that ignores Range (200) restarts the file instead of appending.</summary>
    [Fact]
    public async Task ModelDownloader_ServerIgnoresRange_RestartsFromScratch()
    {
        string dir = NewTempDir();
        string target = Path.Combine(dir, "file.bin");
        byte[] data = new byte[1_000];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(target + ".partial", new byte[400]); // garbage prefix

        string? seenRange = null;
        var handler = new FakeHttpHandler((req, _) =>
        {
            seenRange = req.Headers.Range?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        });
        using var http = new HttpClient(handler);
        var downloader = new ModelDownloader(http, NullLogger<ModelDownloader>.Instance);

        await downloader.DownloadAsync(
            new ModelFile("file", "http://fake/file.bin", target, data.Length, string.Empty),
            new InlineProgress<DownloadProgress>(_ => { }),
            CancellationToken.None);

        seenRange.Should().Be("bytes=400-");
        File.ReadAllBytes(target).Should().Equal(data);
    }

    /// <summary>A SHA-256 mismatch deletes the partial file and throws.</summary>
    [Fact]
    public async Task ModelDownloader_HashMismatch_ThrowsAndDeletesPartial()
    {
        string dir = NewTempDir();
        string target = Path.Combine(dir, "file.bin");
        byte[] data = new byte[500];
        var handler = new FakeHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) }));
        using var http = new HttpClient(handler);
        var downloader = new ModelDownloader(http, NullLogger<ModelDownloader>.Instance);
        var file = new ModelFile("file", "http://fake/file.bin", target, data.Length, new string('0', 64));

        Func<Task> act = () => downloader.DownloadAsync(
            file, new InlineProgress<DownloadProgress>(_ => { }), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
        File.Exists(target).Should().BeFalse();
        File.Exists(target + ".partial").Should().BeFalse();
    }

    /// <summary>Cancel mid-stream leaves the partial file in place and no target.</summary>
    [Fact]
    public async Task ModelDownloader_Cancel_LeavesPartialFile()
    {
        string dir = NewTempDir();
        string target = Path.Combine(dir, "big.bin");
        byte[] data = new byte[1_000_000];
        Random.Shared.NextBytes(data);
        var handler = new FakeHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new SlowStream(data, chunkSize: 8_192, delayMs: 20)),
            }));
        using var http = new HttpClient(handler);
        var downloader = new ModelDownloader(http, NullLogger<ModelDownloader>.Instance);
        var file = new ModelFile("big", "http://fake/big.bin", target, data.Length, string.Empty);
        using var cts = new CancellationTokenSource();
        var progress = new InlineProgress<DownloadProgress>(_ => cts.Cancel());

        Func<Task> act = () => downloader.DownloadAsync(file, progress, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(target).Should().BeFalse();
        File.Exists(target + ".partial").Should().BeTrue();
        new FileInfo(target + ".partial").Length.Should().BeGreaterThan(0);
    }

    /// <summary>Start downloads everything, writes the flag, and asks the shell to close.</summary>
    [Fact]
    public async Task FirstRunViewModel_Start_DownloadsAllWritesFlagAndCloses()
    {
        string dir = NewTempDir();
        string flag = Path.Combine(dir, ".firstrun-complete");
        var handler = new FakeHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[128]) }));
        using var http = new HttpClient(handler);
        var vm = new FirstRunViewModel(
            new ModelDownloader(http, NullLogger<ModelDownloader>.Instance),
            NullLogger<FirstRunViewModel>.Instance,
            baseDirectory: dir,
            flagPath: flag);
        bool closed = false;
        vm.CloseRequested += () => closed = true;

        await vm.StartCommand.ExecuteAsync(null);

        vm.AllComplete.Should().BeTrue();
        vm.OverallProgress.Should().Be(100);
        File.Exists(flag).Should().BeTrue();
        closed.Should().BeTrue();
        handler.Requests.Should().Be(4);
        ModelManifest.Required.Should().OnlyContain(f => File.Exists(ModelManifest.ResolvePath(f, dir)));
        vm.Files.Should().OnlyContain(f => f.IsComplete && f.StatusText == "Complete");
    }

    /// <summary>Files already on disk are skipped: no requests, immediate completion.</summary>
    [Fact]
    public async Task FirstRunViewModel_Start_SkipsFilesAlreadyPresent()
    {
        string dir = NewTempDir();
        string flag = Path.Combine(dir, ".firstrun-complete");
        foreach (ModelFile file in ModelManifest.Required)
        {
            string path = ModelManifest.ResolvePath(file, dir);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[10]);
        }

        var handler = new FakeHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var http = new HttpClient(handler);
        var vm = new FirstRunViewModel(
            new ModelDownloader(http, NullLogger<ModelDownloader>.Instance),
            NullLogger<FirstRunViewModel>.Instance,
            baseDirectory: dir,
            flagPath: flag);

        await vm.StartCommand.ExecuteAsync(null);

        vm.AllComplete.Should().BeTrue();
        handler.Requests.Should().Be(0);
        File.Exists(flag).Should().BeTrue();
    }

    /// <summary>Skip for now writes the flag file and closes, without downloading.</summary>
    [Fact]
    public async Task FirstRunViewModel_Skip_WritesFlagAndRaisesClose()
    {
        string dir = NewTempDir();
        string flag = Path.Combine(dir, ".firstrun-complete");
        var handler = new FakeHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var http = new HttpClient(handler);
        var vm = new FirstRunViewModel(
            new ModelDownloader(http, NullLogger<ModelDownloader>.Instance),
            NullLogger<FirstRunViewModel>.Instance,
            baseDirectory: dir,
            flagPath: flag);
        bool closed = false;
        vm.CloseRequested += () => closed = true;

        await vm.SkipCommand.ExecuteAsync(null);

        File.Exists(flag).Should().BeTrue();
        closed.Should().BeTrue();
        handler.Requests.Should().Be(0);
        vm.AllComplete.Should().BeFalse();
    }

    /// <summary>At most two downloads run concurrently across the four required files.</summary>
    [Fact]
    public async Task FirstRunViewModel_Start_CapsConcurrentDownloadsAtTwo()
    {
        string dir = NewTempDir();
        string flag = Path.Combine(dir, ".firstrun-complete");
        int current = 0;
        int max = 0;
        var handler = new FakeHttpHandler(async (_, ct) =>
        {
            int now = Interlocked.Increment(ref current);
            int observed;
            while (now > (observed = Volatile.Read(ref max)) &&
                   Interlocked.CompareExchange(ref max, now, observed) != observed)
            {
            }

            try
            {
                await Task.Delay(150, ct);
            }
            finally
            {
                Interlocked.Decrement(ref current);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[64]) };
        });
        using var http = new HttpClient(handler);
        var vm = new FirstRunViewModel(
            new ModelDownloader(http, NullLogger<ModelDownloader>.Instance),
            NullLogger<FirstRunViewModel>.Instance,
            baseDirectory: dir,
            flagPath: flag);

        await vm.StartCommand.ExecuteAsync(null);

        vm.AllComplete.Should().BeTrue();
        handler.Requests.Should().Be(4);
        Volatile.Read(ref max).Should().BeLessThanOrEqualTo(2);
        Volatile.Read(ref max).Should().BeGreaterThanOrEqualTo(1);
    }
}