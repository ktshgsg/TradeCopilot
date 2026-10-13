using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TradeCopilot.Core.Cache;
using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Llm;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Orchestrator;
using TradeCopilot.Core.Privacy;
using TradeCopilot.Core.Processing;
using TradeCopilot.Core.Rules;
using TradeCopilot.Core.Speech;
using TradeCopilot.Core.Vision;
using TradeCopilot.Service;

namespace TradeCopilot.Tests;

/// <summary>
/// Task 5 stage 3a tests for the two privacy gates in
/// <see cref="StartupHostedService"/>: the pre-OCR window-title filter and the
/// post-OCR sensitive-content check.
/// </summary>
public sealed class PrivacyGateTests
{
    /// <summary>Capture fake that the test can raise frames on.</summary>
    private sealed class RaisableCapture : IScreenCapture
    {
        public bool IsCapturing { get; private set; }

        public event EventHandler<FrameData>? OnFrameCaptured;

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

        public void Raise(FrameData frame) => OnFrameCaptured?.Invoke(this, frame);
    }

    /// <summary>OCR stub whose result the test controls.</summary>
    private sealed class StubOcr : IOcrEngine
    {
        private readonly OcrResult _result;

        public StubOcr(string text) => _result = new OcrResult(text, [], "en-US");

        public IReadOnlyList<string> SupportedLanguages => ["en-US"];

        public bool IsAvailable => true;

        public int Calls { get; private set; }

        public Task<OcrResult> RecognizeAsync(FrameData frame, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    /// <summary>Recording audit sink so gate writes can be asserted.</summary>
    private sealed class RecordingAudit : IAuditSink
    {
        private readonly List<(string Event, string Meta)> _writes = [];

        public IReadOnlyList<(string Event, string Meta)> Writes
        {
            get
            {
                lock (_writes)
                {
                    return _writes.ToArray();
                }
            }
        }

        public void Write(string eventName, string meta)
        {
            lock (_writes)
            {
                _writes.Add((eventName, meta));
            }
        }
    }

    private static FrameData DummyFrame(long sequence = 1) => new(
        64, 64, 64 * 4, new byte[64 * 64 * 4], DateTime.UtcNow, sequence);

    private static StartupHostedService BuildService(
        RaisableCapture capture,
        string ocrText,
        IWindowFilter filter,
        RecordingAudit audit,
        PrivacyOptions privacy,
        PrivacyGateCounters counters,
        AgentState state,
        out StubOcr ocr)
    {
        ocr = new StubOcr(ocrText);
        var cache = Substitute.For<ISemanticCache>();
        var rules = Substitute.For<IRuleEngine>();
        var llm = Substitute.For<ILlmEngine>();
        var asr = Substitute.For<ISpeechRecognizer>();
        var tts = Substitute.For<ISpeechSynthesizer>();
        var orchestrator = new AgentOrchestrator(
            state, cache, rules, llm, DisabledVisionEngine.Instance, NullLogger<AgentOrchestrator>.Instance);
        var processor = new FrameProcessor(
            new FrameGate(),
            ocr,
            DisabledChartPatternDetector.Instance,
            NullLogger<FrameProcessor>.Instance);
        return new StartupHostedService(
            cache,
            llm,
            asr,
            tts,
            DisabledVisionEngine.Instance,
            state,
            orchestrator,
            capture,
            new AudioCapture(NullLogger<AudioCapture>.Instance),
            processor,
            new AgentOptions { Privacy = privacy },
            NullLogger<StartupHostedService>.Instance,
            filter,
            audit,
            privacy,
            counters);
    }

    /// <summary>An excluded window drops the frame before OCR runs.</summary>
    [Fact]
    public async Task Gate1_ExcludedWindow_DropsFrameBeforeOcr()
    {
        var capture = new RaisableCapture();
        var filter = Substitute.For<IWindowFilter>();
        filter.IsExcluded(Arg.Any<IReadOnlyList<string>>()).Returns(true);
        var audit = new RecordingAudit();
        var counters = new PrivacyGateCounters();
        var state = new AgentState();
        StartupHostedService service = BuildService(
            capture, "RSI 55 trend up", filter, audit,
            new PrivacyOptions(), counters, state, out StubOcr ocr);

        await service.StartAsync(CancellationToken.None);
        try
        {
            capture.Raise(DummyFrame());
            await WaitForAsync(() => counters.ExcludedByWindow == 1, "one window exclusion");
            await Task.Delay(200);
            ocr.Calls.Should().Be(0, "Gate 1 must not call frameProcessor.Post");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        audit.Writes.Should().Contain(("SensitiveSkipped", "reason=window_title"));
        state.Snapshot().FramesProcessed.Should().Be(0);
    }

    /// <summary>A non-excluded window lets the frame through to OCR and state.</summary>
    [Fact]
    public async Task Gate1_NonExcludedWindow_PassesFrame()
    {
        var capture = new RaisableCapture();
        var filter = Substitute.For<IWindowFilter>();
        filter.IsExcluded(Arg.Any<IReadOnlyList<string>>()).Returns(false);
        filter.GetForegroundWindowTitle().Returns("TradingView - Chart");
        var audit = new RecordingAudit();
        var counters = new PrivacyGateCounters();
        var state = new AgentState();
        StartupHostedService service = BuildService(
            capture, "RSI 55 trend up", filter, audit,
            new PrivacyOptions(), counters, state, out StubOcr ocr);

        await service.StartAsync(CancellationToken.None);
        try
        {
            capture.Raise(DummyFrame());
            await WaitForAsync(() => ocr.Calls == 1, "OCR to run once");
            await WaitForAsync(() => state.Snapshot().FramesProcessed == 1, "state to update");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        counters.ExcludedByWindow.Should().Be(0);
        audit.Writes.Should().NotContain(("SensitiveSkipped", "reason=window_title"));
    }

    /// <summary>Sensitive OCR is dropped before AgentState.Update runs.</summary>
    [Fact]
    public async Task Gate2_SensitiveOcr_DropsBeforeStateUpdate()
    {
        var capture = new RaisableCapture();
        var filter = Substitute.For<IWindowFilter>();
        filter.IsExcluded(Arg.Any<IReadOnlyList<string>>()).Returns(false);
        filter.GetForegroundWindowTitle().Returns("TradingView - Chart");
        var audit = new RecordingAudit();
        var counters = new PrivacyGateCounters();
        var state = new AgentState();
        StartupHostedService service = BuildService(
            capture, "Enter your password to continue", filter, audit,
            new PrivacyOptions(), counters, state, out StubOcr ocr);

        await service.StartAsync(CancellationToken.None);
        try
        {
            capture.Raise(DummyFrame());
            await WaitForAsync(() => ocr.Calls == 1, "OCR to run once");
            await WaitForAsync(() => counters.ExcludedBySensitive == 1, "one sensitive exclusion");
            await Task.Delay(200);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        state.Snapshot().FramesProcessed.Should().Be(0, "sensitive OCR must never reach state");
        audit.Writes.Should().Contain(("SensitiveSkipped", "reason=ocr_content"));
    }

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25);
        }

        condition().Should().BeTrue($"expected {what}");
    }

    /// <summary>Benign OCR with a benign title reaches AgentState exactly once.</summary>
    [Fact]
    public async Task Gate2_BenignOcr_UpdatesState()
    {
        var capture = new RaisableCapture();
        var filter = Substitute.For<IWindowFilter>();
        filter.IsExcluded(Arg.Any<IReadOnlyList<string>>()).Returns(false);
        filter.GetForegroundWindowTitle().Returns("TradingView");
        var audit = new RecordingAudit();
        var counters = new PrivacyGateCounters();
        var state = new AgentState();
        StartupHostedService service = BuildService(
            capture, "RSI 55, trend up", filter, audit,
            new PrivacyOptions(), counters, state, out _);

        await service.StartAsync(CancellationToken.None);
        try
        {
            capture.Raise(DummyFrame());
            await WaitForAsync(() => state.Snapshot().FramesProcessed == 1, "state to update once");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        counters.ExcludedBySensitive.Should().Be(0);
    }

    /// <summary>PauseOnSensitive=false bypasses Gate 2 even for sensitive OCR.</summary>
    [Fact]
    public async Task Gate2_Disabled_PassesSensitiveOcr()
    {
        var capture = new RaisableCapture();
        var filter = Substitute.For<IWindowFilter>();
        filter.IsExcluded(Arg.Any<IReadOnlyList<string>>()).Returns(false);
        var audit = new RecordingAudit();
        var counters = new PrivacyGateCounters();
        var state = new AgentState();
        var privacy = new PrivacyOptions { PauseOnSensitive = false };
        StartupHostedService service = BuildService(
            capture, "Enter your password to continue", filter, audit,
            privacy, counters, state, out _);

        await service.StartAsync(CancellationToken.None);
        try
        {
            capture.Raise(DummyFrame());
            await WaitForAsync(() => state.Snapshot().FramesProcessed == 1, "state to update with gate disabled");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        counters.ExcludedBySensitive.Should().Be(0);
        audit.Writes.Should().NotContain(("SensitiveSkipped", "reason=ocr_content"));
    }

    /// <summary>When both gates would fire, Gate 1 wins and Gate 2 is never reached.</summary>
    [Fact]
    public async Task Gates_BothWouldFire_Gate1Wins()
    {
        var capture = new RaisableCapture();
        var filter = Substitute.For<IWindowFilter>();
        filter.IsExcluded(Arg.Any<IReadOnlyList<string>>()).Returns(true);
        var audit = new RecordingAudit();
        var counters = new PrivacyGateCounters();
        var state = new AgentState();
        StartupHostedService service = BuildService(
            capture, "Enter your password to continue", filter, audit,
            new PrivacyOptions(), counters, state, out StubOcr ocr);

        await service.StartAsync(CancellationToken.None);
        try
        {
            capture.Raise(DummyFrame());
            await WaitForAsync(() => counters.ExcludedByWindow == 1, "one window exclusion");
            await Task.Delay(200);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        ocr.Calls.Should().Be(0, "Gate 1 drops the frame so Gate 2 never runs");
        counters.ExcludedBySensitive.Should().Be(0);
        audit.Writes.Should().Contain(("SensitiveSkipped", "reason=window_title"));
    }

    /// <summary>A throwing filter must not crash the capture event handler.</summary>
    [Fact]
    public async Task Gate1_FilterThrows_HandlerSurvives()
    {
        var capture = new RaisableCapture();
        var filter = Substitute.For<IWindowFilter>();
        filter.IsExcluded(Arg.Any<IReadOnlyList<string>>())
            .Returns(_ => throw new InvalidOperationException("P/Invoke failed"));
        var audit = new RecordingAudit();
        var counters = new PrivacyGateCounters();
        var state = new AgentState();
        StartupHostedService service = BuildService(
            capture, "RSI 55 trend up", filter, audit,
            new PrivacyOptions(), counters, state, out StubOcr ocr);

        await service.StartAsync(CancellationToken.None);
        try
        {
            Func<Task> raise = () =>
            {
                capture.Raise(DummyFrame());
                return Task.CompletedTask;
            };
            await raise.Should().NotThrowAsync("the gate catch must swallow filter failures");
            await Task.Delay(200);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        ocr.Calls.Should().Be(0);
        counters.ExcludedByWindow.Should().Be(0);
    }

}
