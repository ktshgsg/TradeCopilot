using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using TradeCopilot.Core.Capture;

namespace TradeCopilot.Core.Vision;

/// <summary>
/// Inference half of <see cref="YoloChartPatternDetector"/>: public surface,
/// letterbox preprocessing, and the guarded ONNX session call.
/// </summary>
public sealed partial class YoloChartPatternDetector
{
    /// <inheritdoc />
    public bool Enabled { get; }

    /// <inheritdoc />
    public Task<IReadOnlyList<PatternDetection>> DetectAsync(FrameData frame, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enabled || _session is null)
        {
            return Task.FromResult<IReadOnlyList<PatternDetection>>(Array.Empty<PatternDetection>());
        }

        return Task.Run<IReadOnlyList<PatternDetection>>(() => DetectCore(frame, ct), ct);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session?.Dispose();
        _inferenceLock.Dispose();
    }

    private static int OutputClassCount(NodeMetadata meta)
    {
        int[] dims = meta.Dimensions;
        if (dims.Length == 3)
        {
            int channels = Math.Min(dims[1], dims[2]);
            if (channels >= 5)
            {
                return Math.Min(channels - 4, ClassLabels.Length);
            }
        }

        return ClassLabels.Length;
    }

    private List<PatternDetection> DetectCore(FrameData frame, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        (float scale, int padX, int padY) = Letterbox(frame, _inputBuffer);
        _inferenceLock.Wait(ct);
        try
        {
            var tensor = new DenseTensor<float>(_inputBuffer.AsMemory(), _inputShape);
            NamedOnnxValue input = NamedOnnxValue.CreateFromTensor(_inputName, tensor);
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _session!.Run([input]);
            Tensor<float> output = outputs[0].AsTensor<float>();
            return Decode(output.ToArray(), output.Dimensions.ToArray(), scale, padX, padY, frame.Width, frame.Height, _classCount);
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    internal static (float Scale, int PadX, int PadY) Letterbox(FrameData frame, float[] dst)
    {
        int srcW = frame.Width;
        int srcH = frame.Height;
        float scale = Math.Min((float)InputSize / srcW, (float)InputSize / srcH);
        int scaledW = Math.Max(1, (int)MathF.Round(srcW * scale));
        int scaledH = Math.Max(1, (int)MathF.Round(srcH * scale));
        int padX = (InputSize - scaledW) / 2;
        int padY = (InputSize - scaledH) / 2;
        Array.Clear(dst);

        byte[] src = frame.Pixels;
        int plane = InputSize * InputSize;
        for (int y = 0; y < scaledH; y++)
        {
            int sy = Math.Min(srcH - 1, y * srcH / scaledH);
            int srcRow = sy * frame.Stride;
            int dstRow = ((y + padY) * InputSize) + padX;
            for (int x = 0; x < scaledW; x++)
            {
                int sx = Math.Min(srcW - 1, x * srcW / scaledW);
                int si = srcRow + (sx * 4);
                int di = dstRow + x;
                dst[di] = src[si + 2] / 255f;           // R (source is BGRA)
                dst[plane + di] = src[si + 1] / 255f;   // G
                dst[(2 * plane) + di] = src[si] / 255f; // B
            }
        }

        return (scale, padX, padY);
    }
}
