using FluentAssertions;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Orchestrator;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Tests;

/// <summary>
/// Task 5 stage 1 tests for <see cref="AgentState"/>: snapshot semantics, context-hash
/// stability, clearing, and thread safety under concurrent updates and reads.
/// </summary>
public sealed class AgentStateTests
{
    private static OcrResult Ocr(string text = "RSI: 72 MACD: 0.30") => new(text, [], "en-US");

    private static PatternDetection Pattern() => new(PatternLabels.BullFlag, 0.8f, new Rect(0, 0, 10, 10));

    /// <summary>A fresh state has no OCR, no patterns, an empty hash, and zero frames.</summary>
    [Fact]
    public void Snapshot_OnFreshState_IsEmpty()
    {
        var state = new AgentState();

        AgentSnapshot snapshot = state.Snapshot();

        snapshot.Ocr.Should().BeNull();
        snapshot.Patterns.Should().BeEmpty();
        snapshot.ContextHash.Should().BeEmpty();
        snapshot.FramesProcessed.Should().Be(0);
    }

    /// <summary>Updating with OCR produces a context hash and increments the frame counter.</summary>
    [Fact]
    public void Update_WithOcr_SetsContextHashAndIncrementsFrames()
    {
        var state = new AgentState();

        state.Update(Ocr(), [Pattern()]);

        AgentSnapshot snapshot = state.Snapshot();
        snapshot.Ocr.Should().NotBeNull();
        snapshot.Patterns.Should().HaveCount(1);
        snapshot.ContextHash.Should().NotBeNullOrEmpty();
        snapshot.FramesProcessed.Should().Be(1);
    }

    /// <summary>The same context twice yields the same hash; the frame counter still advances.</summary>
    [Fact]
    public void Update_TwiceWithSameOcr_KeepsHashStable()
    {
        var state = new AgentState();

        state.Update(Ocr(), [Pattern()]);
        string first = state.Snapshot().ContextHash;
        state.Update(Ocr(), [Pattern()]);
        AgentSnapshot snapshot = state.Snapshot();

        snapshot.ContextHash.Should().Be(first);
        snapshot.FramesProcessed.Should().Be(2);
    }

    /// <summary>Updating with a null OCR clears the context hash.</summary>
    [Fact]
    public void Update_WithNullOcr_ClearsContextHash()
    {
        var state = new AgentState();
        state.Update(Ocr(), [Pattern()]);

        state.Update(null, []);

        AgentSnapshot snapshot = state.Snapshot();
        snapshot.Ocr.Should().BeNull();
        snapshot.ContextHash.Should().BeEmpty();
        snapshot.Patterns.Should().BeEmpty();
    }

    /// <summary>Clear resets OCR, patterns, hash, and the frame counter.</summary>
    [Fact]
    public void Clear_ResetsEverything()
    {
        var state = new AgentState();
        state.Update(Ocr(), [Pattern()]);

        state.Clear();

        AgentSnapshot snapshot = state.Snapshot();
        snapshot.Ocr.Should().BeNull();
        snapshot.Patterns.Should().BeEmpty();
        snapshot.ContextHash.Should().BeEmpty();
        snapshot.FramesProcessed.Should().Be(0);
    }

    /// <summary>Mutating a returned pattern array must not affect later snapshots.</summary>
    [Fact]
    public void Snapshot_ReturnsDefensiveCopy()
    {
        var state = new AgentState();
        state.Update(Ocr(), [Pattern()]);

        AgentSnapshot first = state.Snapshot();
        var mutable = first.Patterns as PatternDetection[];
        mutable.Should().NotBeNull("the snapshot exposes an array the caller can mutate");
        mutable![0] = new PatternDetection(PatternLabels.BearFlag, 0.1f, new Rect(1, 1, 2, 2));

        AgentSnapshot second = state.Snapshot();
        second.Patterns.Should().HaveCount(1);
        second.Patterns[0].Label.Should().Be(PatternLabels.BullFlag);
    }

    /// <summary>400 concurrent updates and snapshots neither throw nor lose increments.</summary>
    [Fact]
    public void Update_AndSnapshot_AreThreadSafe()
    {
        var state = new AgentState();

        var act = () =>
        {
            Parallel.For(0, 4, worker =>
            {
                for (int i = 0; i < 100; i++)
                {
                    state.Update(Ocr(), [Pattern()]);

                    // The parameter is also named "worker" so the discard below is unambiguous.
                    string hash = state.Snapshot().ContextHash;
                    _ = hash.Length;
                }
            });
        };

        act.Should().NotThrow();
        state.Snapshot().FramesProcessed.Should().Be(400);
    }
}
