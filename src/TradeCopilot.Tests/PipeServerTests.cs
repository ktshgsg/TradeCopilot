using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TradeCopilot.Core.Cache;
using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Llm;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Orchestrator;
using TradeCopilot.Core.Rules;
using TradeCopilot.Core.Speech;
using TradeCopilot.Core.Vision;
using TradeCopilot.Service;

namespace TradeCopilot.Tests;

/// <summary>
/// Task 5 stage 2 integration test: drives <see cref="PipeServer"/> over a real named
/// pipe using its own host and a unique pipe name, so it never collides with the
/// production service.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PipeServerTests
{
    /// <summary>In-memory capture whose active flag the tests can assert on.</summary>
    private sealed class FakeScreenCapture : IScreenCapture
    {
        public bool IsCapturing { get; private set; }

#pragma warning disable CS0067 // Never raised: no frames are produced in this fake.
        public event EventHandler<FrameData>? OnFrameCaptured;
#pragma warning restore CS0067

        public Task StartAsync(CaptureRegion region, int targetFps, CancellationToken ct = default)
        {
            IsCapturing = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            IsCapturing = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Skips the pipe tests when running non-interactively (CI/headless).</summary>
    internal sealed class RequiresInteractiveFactAttribute : FactAttribute
    {
        public RequiresInteractiveFactAttribute()
        {
            if (!Environment.UserInteractive)
            {
                Skip = "Named-pipe integration tests require an interactive session.";
            }
        }
    }

    private static AgentOptions BuildOptions(string pipeName) => new()
    {
        Pipe = { Name = pipeName },
    };

    /// <summary>Builds a private host with only what PipeServer needs, and starts it.</summary>
    private static async Task<IHost> StartServerAsync(AgentOptions opts)
    {
        var state = new AgentState();
        state.Update(new OcrResult("RSI: 72", [], "en-US"), Array.Empty<PatternDetection>());

        var cache = Substitute.For<ISemanticCache>();
        cache.QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CacheLookupResult(false, null, 0f, false, null));

        var rules = Substitute.For<IRuleEngine>();
        rules.Evaluate(Arg.Any<string>(), Arg.Any<OcrResult>(), Arg.Any<IReadOnlyList<PatternDetection>>())
            .Returns(RuleResult.NoMatch);

        var llm = Substitute.For<ILlmEngine>();
        llm.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("stub answer");

        var asr = Substitute.For<ISpeechRecognizer>();
        asr.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns("stub transcript");

        var capture = new FakeScreenCapture();
        var orchestrator = new AgentOrchestrator(
            state, cache, rules, llm, DisabledVisionEngine.Instance, NullLogger<AgentOrchestrator>.Instance);

        IHost host = new HostBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureServices(services =>
            {
                services.AddSingleton(opts);
                services.AddSingleton(state);
                services.AddSingleton(capture);
                services.AddSingleton(asr);
                services.AddSingleton<IHostedService>(sp => new PipeServer(
                    orchestrator,
                    state,
                    capture,
                    asr,
                    opts,
                    sp.GetRequiredService<ILoggerFactory>().CreateLogger<PipeServer>()));
            })
            .Build();

        await host.StartAsync();
        return host;
    }

    /// <summary>Sends one request line and parses the single response line.</summary>
    private static async Task<JsonElement> SendAsync(string pipeName, string requestJson)
    {
        using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);

        byte[] payload = Encoding.UTF8.GetBytes(requestJson + "\n");
        await client.WriteAsync(payload, CancellationToken.None);
        await client.FlushAsync(CancellationToken.None);

        var buffer = new byte[8192];
        using var received = new MemoryStream();
        while (received.Length == 0)
        {
            int read = await client.ReadAsync(buffer, CancellationToken.None);
            if (read == 0)
            {
                break;
            }

            received.Write(buffer, 0, read);
        }

        string line = Encoding.UTF8.GetString(received.ToArray()).Split('\n')[0];
        line.Should().NotBeNullOrEmpty("the server must answer every request with one JSON line");
        using var doc = JsonDocument.Parse(line);
        return doc.RootElement.Clone();
    }

    /// <summary>Status, query, pause/resume, and panic all round-trip over the pipe.</summary>
    [RequiresInteractiveFact]
    public async Task PipeServer_HandlesStatusQueryPauseAndPanic()
    {
        string pipeName = $"TradeCopilot.TestPipe.{Guid.NewGuid():N}";
        IHost host = await StartServerAsync(BuildOptions(pipeName));
        try
        {
            JsonElement status = await SendAsync(pipeName, """{"type":"status"}""");
            status.GetProperty("type").GetString().Should().Be("status");
            status.GetProperty("version").GetString().Should().Be(PipeServer.ServiceVersion);
            status.GetProperty("captureActive").GetBoolean().Should().BeFalse();
            status.GetProperty("framesProcessed").GetInt64().Should().Be(1);
            status.TryGetProperty("uptimeSec", out _).Should().BeTrue();

            JsonElement answer = await SendAsync(pipeName, """{"type":"query","text":"hello"}""");
            answer.GetProperty("type").GetString().Should().Be("answer");
            answer.GetProperty("text").GetString().Should().Be("stub answer");
            answer.GetProperty("source").GetString().Should().Be("llm");

            (await SendAsync(pipeName, """{"type":"pause_capture"}"""))
                .GetProperty("type").GetString().Should().Be("ok");
            (await SendAsync(pipeName, """{"type":"status"}"""))
                .GetProperty("captureActive").GetBoolean().Should().BeFalse();

            (await SendAsync(pipeName, """{"type":"resume_capture"}"""))
                .GetProperty("type").GetString().Should().Be("ok");
            (await SendAsync(pipeName, """{"type":"status"}"""))
                .GetProperty("captureActive").GetBoolean().Should().BeTrue();

            (await SendAsync(pipeName, """{"type":"panic"}"""))
                .GetProperty("type").GetString().Should().Be("ok");

            JsonElement afterPanic = await SendAsync(pipeName, """{"type":"status"}""");
            afterPanic.GetProperty("captureActive").GetBoolean().Should().BeFalse();
            afterPanic.GetProperty("framesProcessed").GetInt64().Should().Be(0);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>Malformed and unknown requests get an error line, and the server survives.</summary>
    [RequiresInteractiveFact]
    public async Task PipeServer_RejectsBadRequests_AndKeepsServing()
    {
        string pipeName = $"TradeCopilot.TestPipe.{Guid.NewGuid():N}";
        IHost host = await StartServerAsync(BuildOptions(pipeName));
        try
        {
            (await SendAsync(pipeName, "not json at all"))
                .GetProperty("type").GetString().Should().Be("error");

            (await SendAsync(pipeName, """{"type":"no_such_command"}"""))
                .GetProperty("type").GetString().Should().Be("error");

            (await SendAsync(pipeName, """{"type":"transcribe"}"""))
                .GetProperty("type").GetString().Should().Be("error");

            // The accept loop is still alive after three bad requests.
            (await SendAsync(pipeName, """{"type":"status"}"""))
                .GetProperty("type").GetString().Should().Be("status");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>Base64 audio is decoded and handed to the recognizer.</summary>
    [RequiresInteractiveFact]
    public async Task PipeServer_TranscribesBase64Audio()
    {
        string pipeName = $"TradeCopilot.TestPipe.{Guid.NewGuid():N}";
        IHost host = await StartServerAsync(BuildOptions(pipeName));
        try
        {
            string audio = Convert.ToBase64String(new byte[320]);

            JsonElement result = await SendAsync(
                pipeName,
                $$"""{"type":"transcribe","audioBase64":"{{audio}}"}""");

            result.GetProperty("type").GetString().Should().Be("answer");
            result.GetProperty("text").GetString().Should().Be("stub transcript");
            result.GetProperty("source").GetString().Should().Be("asr");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
