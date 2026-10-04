using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using TradeCopilot.Core.Capture;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using CoreOcrWord = TradeCopilot.Core.Ocr.OcrWord;
using WinOcrResult = Windows.Media.Ocr.OcrResult;

namespace TradeCopilot.Core.Ocr;

public sealed partial class WindowsOcrEngine
{
    /// <inheritdoc />
    public async Task<OcrResult> RecognizeAsync(FrameData frame, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_engine is null || !IsAvailable)
        {
            return OcrResult.Empty;
        }

        ct.ThrowIfCancellationRequested();
        try
        {
            (int width, int height, byte[] pixels) = Downscale(frame, MaxOcrWidth);
            using SoftwareBitmap bitmap = await BgraToSoftwareBitmapAsync(width, height, pixels, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            WinOcrResult result = await _engine.RecognizeAsync(bitmap).AsTask(ct).ConfigureAwait(false);
            double scaleX = (double)frame.Width / width;
            double scaleY = (double)frame.Height / height;
            var words = new List<CoreOcrWord>();
            foreach (Windows.Media.Ocr.OcrLine line in result.Lines)
            {
                foreach (Windows.Media.Ocr.OcrWord word in line.Words)
                {
                    Windows.Foundation.Rect r = word.BoundingRect;
                    words.Add(new CoreOcrWord(
                        word.Text,
                        new Windows.Foundation.Rect(r.X * scaleX, r.Y * scaleY, r.Width * scaleX, r.Height * scaleY)));
                }
            }

            return new OcrResult(result.Text, words, LanguageTag);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Windows OCR recognition failed; returning empty result.");
            return OcrResult.Empty;
        }
    }

    private static async Task<SoftwareBitmap> BgraToSoftwareBitmapAsync(
        int width, int height, byte[] pixels, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(pixels.AsBuffer()).AsTask(ct).ConfigureAwait(false);
        stream.Seek(0);
        var readBuffer = new Windows.Storage.Streams.Buffer((uint)pixels.Length);
        IBuffer buffer = await stream.ReadAsync(
            readBuffer, (uint)pixels.Length, InputStreamOptions.None).AsTask(ct).ConfigureAwait(false);
        return SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
    }
}
