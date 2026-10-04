using TradeCopilot.Core.Capture;

namespace TradeCopilot.Core.Processing;

/// <summary>
/// Drops near-duplicate frames by comparing dHash Hamming distance against a threshold.
/// </summary>
public sealed class FrameGate
{
    private readonly object _sync = new();
    private bool _hasHash;

    /// <summary>
    /// Creates a gate that accepts frames whose hash distance is at least the threshold.
    /// </summary>
    /// <param name="hammingThreshold">Minimum bit distance that counts as a new frame.</param>
    public FrameGate(int hammingThreshold = 5)
    {
        if (hammingThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hammingThreshold), "Threshold must be non-negative.");
        }

        HammingThreshold = hammingThreshold;
    }

    /// <summary>Minimum bit distance that counts as a new frame.</summary>
    public int HammingThreshold { get; }

    /// <summary>Hash of the most recently accepted frame.</summary>
    public ulong LastHash { get; private set; }

    /// <summary>Number of frames dropped as duplicates.</summary>
    public long SkippedCount { get; private set; }

    /// <summary>Number of frames accepted for processing.</summary>
    public long AcceptedCount { get; private set; }

    /// <summary>
    /// Returns true for the first frame and for frames different enough from the last accepted one.
    /// </summary>
    /// <param name="frame">Candidate frame.</param>
    /// <returns>True when the frame should be processed.</returns>
    public bool ShouldProcess(FrameData frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ulong hash = PerceptualHash.Compute(frame);
        lock (_sync)
        {
            if (!_hasHash)
            {
                _hasHash = true;
                LastHash = hash;
                AcceptedCount++;
                return true;
            }

            if (PerceptualHash.HammingDistance(LastHash, hash) >= HammingThreshold)
            {
                LastHash = hash;
                AcceptedCount++;
                return true;
            }

            SkippedCount++;
            return false;
        }
    }
}
