using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TradeCopilot.Core.Cache;
using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Llm;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Orchestrator;
using TradeCopilot.Core.Processing;
using TradeCopilot.Core.Rules;
using TradeCopilot.Core.Speech;
using TradeCopilot.Core.Vision;
using TradeCopilot.Service;

namespace TradeCopilot.Tests;

/// <summary>
/// Task 5 stage 2 tests for <see cref="StartupHostedService"/>: the ordered startup log
/// sequence and a prompt, non-hanging shutdown.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StartupHostedServiceTests
{
    private sealed class FakeScreenCapture : IScreenCapture
    {
        public bool IsCapturing { get; private set; }

        public int StopCalls { get; private set; }

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
            StopCalls++;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Captures log messages so the startup sequence can be asserted.</summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return _messages.ToArray();
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly RecordingLoggerProvider _owner;

            public RecordingLogger(RecordingLoggerProvider owner) => _owner = owner;

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (_owner._messages)
                {
                    _owner._messages.Add(formatter(state, exception));
                }
            }
        }
    }

    /// <summary>Builds the service over substitutes so no DXGI device is needed.</summary>
    private static (StartupHostedService Service, FakeScreenCapture Capture, RecordingLoggerProvider Logs) Build()
    {
        var state = new AgentState();
        var cache = Substitute.For<ISemanticCache>();
        var rules = Substitute.For<IRuleEngine>();
        rules.Evaluate(Arg.Any<string>(), Arg.Any<OcrResult>(), Arg.Any<IReadOnlyList<PatternDetection>>())
            .Returns(RuleResult.NoMatch);
        var llm = Substitute.For<ILlmEngine>();
        llm.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("warm answer");
        var asr = Substitute.For<ISpeechRecognizer>();
        var tts = Substitute.For<ISpeechSynthesizer>();
        var capture = new FakeScreenCapture();

        var orchestrator = new AgentOrchestrator(
            state, cache, rules, llm, DisabledVisionEngine.Instance, NullLogger<AgentOrchestrator>.Instance);

        var frameProcessor = new FrameProcessor(
            new FrameGate(),
            Substitute.For<IOcrEngine>(),
            DisabledChartPatternDetector.Instance,
            NullLogger<FrameProcessor>.Instance);

        var logs = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(logs);
        });

        var service = new StartupHostedService(
            cache,
            llm,
            asr,
            tts,
            DisabledVisionEngine.Instance,
            state,
            orchestrator,
            capture,
            new AudioCapture(NullLogger<AudioCapture>.Instance),
            frameProcessor,
            new AgentOptions(),
            loggerFactory.CreateLogger<StartupHostedService>());

        return (service, capture, logs);
    }

    /// <summary>Startup logs starting, engine init, warm-up, then Ready — in that order.</summary>
    [Fact]
    public async Task StartAsync_LogsExpectedSequenceInOrder()
    {
        (StartupHostedService service, FakeScreenCapture capture, RecordingLoggerProvider logs) = Build();

        await service.StartAsync(CancellationToken.None);

        List<string> messages = logs.Messages.ToList();
        int starting = messages.FindIndex(m => m.Contains("TradeCopilot starting", StringComparison.Ordinal));
        int cache = messages.FindIndex(m => m.Contains("Cache init:", StringComparison.Ordinal));
        int llm = messages.FindIndex(m => m.Contains("LLM init:", StringComparison.Ordinal));
        int asr = messages.FindIndex(m => m.Contains("ASR init:", StringComparison.Ordinal));
        int tts = messages.FindIndex(m => m.Contains("TTS init:", StringComparison.Ordinal));
        int warmup = messages.FindIndex(m => m.Contains("Warm-up completed", StringComparison.Ordinal));
        int ready = messages.FindIndex(m => m.Contains("Ready", StringComparison.Ordinal));

        starting.Should().BeGreaterThanOrEqualTo(0);
        cache.Should().BeGreaterThan(starting);
        llm.Should().BeGreaterThan(cache);
        asr.Should().BeGreaterThan(llm);
        tts.Should().BeGreaterThan(asr);
        warmup.Should().BeGreaterThan(tts);
        ready.Should().BeGreaterThan(warmup);

        // A missing model is reported as disabled, not as a startup failure.
        messages.Should().Contain(m => m.Contains("LLM init: enabled=False in", StringComparison.Ordinal));
        capture.IsCapturing.Should().BeTrue();
        service.Started.Should().BeTrue();

        await service.StopAsync(CancellationToken.None);
    }

    /// <summary>StopAsync stops capture, unwires events, and returns well under 5 s.</summary>
    [Fact]
    public async Task StopAsync_CompletesPromptly()
    {
        (StartupHostedService service, FakeScreenCapture capture, RecordingLoggerProvider logs) = Build();
        await service.StartAsync(CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        await service.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        capture.StopCalls.Should().Be(1);
        capture.IsCapturing.Should().BeFalse();
        service.Started.Should().BeFalse();
        logs.Messages.Should().Contain("Stopped");
    }
}
