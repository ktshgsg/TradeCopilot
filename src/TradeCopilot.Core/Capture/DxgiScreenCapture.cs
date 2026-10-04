using System.Buffers;
using Microsoft.Extensions.Logging;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DXGI;
using D3D11MapFlags = Vortice.Direct3D11.MapFlags;

namespace TradeCopilot.Core.Capture;

public sealed partial class DxgiScreenCapture : IScreenCapture
{
    private void CaptureLoop(CancellationToken ct)
    {
        _capturing = true;
        try
        {
            int frameBudgetMs = Math.Max(1, 1000 / _targetFps);
            while (!ct.IsCancellationRequested)
            {
                long tickStart = Environment.TickCount64;
                bool released = false;
                try
                {
                    IDXGIOutputDuplication? duplication = _duplication;
                    ID3D11Device? device = _device;
                    if (duplication is null || device is null)
                    {
                        ReinitializeDuplication();
                        Thread.Sleep(Math.Min(frameBudgetMs, AcquireTimeoutMs));
                        continue;
                    }

                    Result acquire = duplication.AcquireNextFrame(
                        AcquireTimeoutMs, out OutduplFrameInfo _, out IDXGIResource? desktopResource);
                    if (acquire == Vortice.DXGI.ResultCode.WaitTimeout)
                    {
                        continue;
                    }

                    if (acquire == Vortice.DXGI.ResultCode.AccessLost)
                    {
                        ReinitializeDuplication();
                        continue;
                    }

                    acquire.CheckError();
                    if (desktopResource is null)
                    {
                        continue;
                    }

                    using (desktopResource)
                    {
                        using ID3D11Texture2D desktopTexture = desktopResource.QueryInterface<ID3D11Texture2D>();
                        duplication.ReleaseFrame();
                        released = true;
                        FrameData frame = CopyFrameToCpu(desktopTexture);
                        OnFrameCaptured?.Invoke(this, frame);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Capture loop iteration failed; continuing.");
                }
                finally
                {
                    if (!released)
                    {
                        try
                        {
                            _duplication?.ReleaseFrame();
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex, "ReleaseFrame failed; reinitializing duplication.");
                            ReinitializeDuplication();
                        }
                    }
                }

                int elapsed = (int)(Environment.TickCount64 - tickStart);
                int sleep = frameBudgetMs - elapsed;
                if (sleep > 0)
                {
                    Thread.Sleep(sleep);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Capture loop terminated unexpectedly.");
        }
        finally
        {
            _capturing = false;
        }
    }
    private FrameData CopyFrameToCpu(ID3D11Texture2D desktopTexture)
    {
        ID3D11DeviceContext context = _device!.ImmediateContext;
        Texture2DDescription srcDesc = desktopTexture.Description;

        lock (_sync)
        {
            if (_staging is null || _staging.Description.Width != srcDesc.Width || _staging.Description.Height != srcDesc.Height)
            {
                _staging?.Dispose();
                var stagingDesc = new Texture2DDescription(
                    srcDesc.Format,
                    srcDesc.Width,
                    srcDesc.Height,
                    1,
                    1,
                    BindFlags.None,
                    ResourceUsage.Staging,
                    CpuAccessFlags.Read,
                    1,
                    0,
                    ResourceOptionFlags.None);
                _staging = _device.CreateTexture2D(stagingDesc);
            }
        }

        ID3D11Texture2D staging = _staging!;
        context.CopyResource(staging, desktopTexture);
        MappedSubresource map = context.Map(staging, 0, MapMode.Read, D3D11MapFlags.None);
        try
        {
            int srcWidth = (int)srcDesc.Width;
            int srcHeight = (int)srcDesc.Height;
            int rowPitch = (int)map.RowPitch;

            int cropX = Math.Clamp(_region.X, 0, Math.Max(0, srcWidth - 1));
            int cropY = Math.Clamp(_region.Y, 0, Math.Max(0, srcHeight - 1));
            int cropW = _region.Width <= 0 ? srcWidth - cropX : Math.Min(_region.Width, srcWidth - cropX);
            int cropH = _region.Height <= 0 ? srcHeight - cropY : Math.Min(_region.Height, srcHeight - cropY);
            if (cropW <= 0 || cropH <= 0)
            {
                throw new InvalidOperationException("Capture region is outside the desktop bounds.");
            }

            int stride = cropW * 4;
            byte[] rented = ArrayPool<byte>.Shared.Rent(stride * cropH);
            unsafe
            {
                byte* src = (byte*)map.DataPointer.ToPointer();
                fixed (byte* dst = rented)
                {
                    for (int y = 0; y < cropH; y++)
                    {
                        byte* srcRow = src + (((y + cropY) * rowPitch) + (cropX * 4));
                        byte* dstRow = dst + (y * stride);
                        for (int x = 0; x < stride; x++)
                        {
                            dstRow[x] = srcRow[x];
                        }
                    }
                }
            }

            long sequence = Interlocked.Increment(ref _sequence);
            return new FrameData(cropW, cropH, stride, rented, DateTime.UtcNow, sequence);
        }
        finally
        {
            context.Unmap(staging, 0);
        }
    }
}

