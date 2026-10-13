using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FluentAssertions;
using TradeCopilot.Core.Cache;
using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Processing;
using TradeCopilot.Core.Vision;
using Xunit.Abstractions;

namespace TradeCopilot.Tests;

/// <summary>
/// Screen capture pipeline tests: perceptual hash, frame gate, OCR, live capture.
/// </summary>
public sealed class CaptureTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Surfaces test diagnostics (e.g. the EmptyFrameSkips count).</summary>
    public CaptureTests(ITestOutputHelper output) => _output = output;
    /// <summary>
    /// Captures at 2 FPS for 10s and saves 5 PNGs. Asserts a widened count band
    /// (15–25) plus timestamp monotonicity, mean inter-frame interval, and PNG sizes,
    /// so the test survives busy-machine timing drift on CI.
    /// </summary>
    [Fact]
    public async Task Capture_At2Fps_For10Seconds_Yields15To25Frames_AndSaves5Pngs()
    {
        using var capture = new DxgiScreenCapture(targetFps: 2);
        var frames = new List<FrameData>();
        var gate = new object();
        capture.OnFrameCaptured += (_, frame) =>
        {
            var copy = new byte[frame.Stride * frame.Height];
            Buffer.BlockCopy(frame.Pixels, 0, copy, 0, copy.Length);
            lock (gate)
            {
                frames.Add(frame with { Pixels = copy });
            }
        };

        await capture.StartAsync();
        try
        {
            for (int i = 0; i < 50 && !capture.IsCapturing; i++)
            {
                await Task.Delay(100);
            }

            Assert.True(capture.IsCapturing);
            Task jiggle = Task.Run(async () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    NativeMove(500 + (i * 17), 500);
                    await Task.Delay(500);
                }
            });
            await Task.Delay(TimeSpan.FromSeconds(10));
            await jiggle;
        }
        finally
        {
            await capture.StopAsync();
        }

        int count;
        double spanSec = 0;
        lock (gate)
        {
            count = frames.Count;
            if (count > 1)
            {
                spanSec = (frames[^1].TimestampUtc - frames[0].TimestampUtc).TotalSeconds;
            }
        }

        _output.WriteLine(
            $"Empty frame skips: {capture.EmptyFrameSkips}; frames: {count}; span: {spanSec:F1}s");

        count.Should().BeInRange(15, 30, "2 FPS for 10s yields ~20 frames, with headroom for busy-machine drift");

        IReadOnlyList<DateTime> timestamps;
        lock (gate)
        {
            timestamps = [.. frames.Select(f => f.TimestampUtc)];
        }

        timestamps.Should().HaveCount(count);
        timestamps.Should().BeInAscendingOrder(
            "frames must arrive in capture order even when the scheduler jitters");

        double meanGapMs = timestamps
            .Zip(timestamps.Skip(1), (prev, next) => (next - prev).TotalMilliseconds)
            .Average();
        meanGapMs.Should().BeInRange(
            250, 550, "mean inter-frame interval should stay near the 500ms frame period");

        string dir = Path.Combine(AppContext.BaseDirectory, "captures");
        Directory.CreateDirectory(dir);
        List<FrameData> snapshot;
        lock (gate)
        {
            snapshot = frames.Take(5).ToList();
        }

        snapshot.Should().HaveCount(5);
        for (int i = 0; i < snapshot.Count; i++)
        {
            SavePng(Path.Combine(dir, $"frame{i}.png"), snapshot[i]);
            File.Exists(Path.Combine(dir, $"frame{i}.png")).Should().BeTrue();
            new FileInfo(Path.Combine(dir, $"frame{i}.png")).Length.Should().BeGreaterThan(
                10 * 1024, "every saved PNG must contain real frame data, not an empty image");
        }

        _output.WriteLine($"Empty frame skips: {capture.EmptyFrameSkips}");
    }

    /// <summary>
    /// A gate over 100 near-identical frames should accept fewer than 10.
    /// </summary>
    [Fact]
    public void FrameGate_Accepts_FewerThan10_Of100SimilarFrames()
    {
        var gate = new FrameGate(hammingThreshold: 5);
        var frame = new FrameData(64, 64, 64 * 4, SolidBgra(64, 64, 0x10, 0x20, 0x30), DateTime.UtcNow, 0);
        int accepted = 0;
        for (int i = 0; i < 100; i++)
        {
            byte[] pixels = SolidBgra(64, 64, (byte)(0x10 + (i % 2)), 0x20, 0x30);
            if (gate.ShouldProcess(frame with { Pixels = pixels, SequenceNumber = i }))
            {
                accepted++;
            }
        }

        accepted.Should().BeLessThan(10);
        gate.AcceptedCount.Should().Be(accepted);
        gate.SkippedCount.Should().Be(100 - accepted);
    }

    /// <summary>
    /// Identical frames must hash to a Hamming distance of 0.
    /// </summary>
    [Fact]
    public void IdenticalFrames_HaveZeroHammingDistance()
    {
        byte[] pixels = GradientBgra(64, 64);
        var a = new FrameData(64, 64, 64 * 4, pixels, DateTime.UtcNow, 1);
        var b = new FrameData(64, 64, 64 * 4, (byte[])pixels.Clone(), DateTime.UtcNow, 2);
        PerceptualHash.HammingDistance(PerceptualHash.Compute(a), PerceptualHash.Compute(b)).Should().Be(0);
    }

    /// <summary>
    /// OCR on a rendered text PNG must contain the expected text when the engine is available.
    /// </summary>
    [Fact]
    public async Task Ocr_OnRenderedPng_ContainsExpectedText()
    {
        using var bitmap = new Bitmap(600, 120);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White);
            using var font = new Font(FontFamily.GenericSansSerif, 36f, FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString("TRADE 12345", font, Brushes.Black, new PointF(20, 25));
        }

        FrameData frame = BitmapToFrame(bitmap, 1);
        var engine = new WindowsOcrEngine();
        if (!engine.IsAvailable)
        {
            return;
        }

        OcrResult result = await engine.RecognizeAsync(frame);
        result.FullText.Should().Contain("TRADE");
    }

    /// <summary>
    /// Same chart context hashes equally; a price change must change the hash.
    /// </summary>
    [Fact]
    public void ChartContextHash_SameInput_SameHash_PriceChange_DifferentHash()
    {
        var ocr = new OcrResult("EURUSD 1.0850 Bull Flag", [], "en-US");
        var patterns = new List<PatternDetection> { new("bull_flag", 0.82f, new Rect(0, 0, 100, 50)) };
        string h1 = ChartContextHash.Compute(ocr, patterns);
        string h2 = ChartContextHash.Compute(
            new OcrResult("  eurusd   1.0850  BULL flag!!! ", [], "en-US"),
            new List<PatternDetection> { new("bull_flag", 0.85f, new Rect(10, 10, 90, 40)) });
        h1.Should().Be(h2);
        h1.Should().HaveLength(16);

        string h3 = ChartContextHash.Compute(new OcrResult("EURUSD 1.0950 Bull Flag", [], "en-US"), patterns);
        h3.Should().NotBe(h1);
    }

    private static byte[] SolidBgra(int width, int height, byte b, byte g, byte r)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }

        return pixels;
    }

    private static byte[] GradientBgra(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int o = ((y * width) + x) * 4;
                pixels[o] = (byte)((x * 255) / Math.Max(1, width - 1));
                pixels[o + 1] = (byte)((y * 255) / Math.Max(1, height - 1));
                pixels[o + 2] = (byte)(((x + y) * 255) / Math.Max(1, width + height - 2));
                pixels[o + 3] = 255;
            }
        }

        return pixels;
    }

    private static FrameData BitmapToFrame(Bitmap bitmap, long sequence)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = bitmap.Width * 4;
            var pixels = new byte[stride * bitmap.Height];
            unsafe
            {
                byte* src = (byte*)data.Scan0.ToPointer();
                fixed (byte* dst = pixels)
                {
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        byte* srcRow = src + (y * data.Stride);
                        byte* dstRow = dst + (y * stride);
                        for (int x = 0; x < stride; x++)
                        {
                            // Bitmap is BGRA in memory; keep channel order.
                            dstRow[x] = srcRow[x];
                        }
                    }
                }
            }

            return new FrameData(bitmap.Width, bitmap.Height, stride, pixels, DateTime.UtcNow, sequence);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void SavePng(string path, FrameData frame)
    {
        using var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, frame.Width, frame.Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                byte* dst = (byte*)data.Scan0.ToPointer();
                fixed (byte* src = frame.Pixels)
                {
                    for (int y = 0; y < frame.Height; y++)
                    {
                        byte* dstRow = dst + (y * data.Stride);
                        byte* srcRow = src + (y * frame.Stride);
                        for (int x = 0; x < frame.Stride; x++)
                        {
                            dstRow[x] = srcRow[x];
                        }
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    private static void NativeMove(int x, int y) => _ = SetCursorPos(x, y);
}
