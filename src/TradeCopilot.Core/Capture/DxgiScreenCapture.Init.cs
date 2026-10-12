using Microsoft.Extensions.Logging;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace TradeCopilot.Core.Capture;

public sealed partial class DxgiScreenCapture
{
    private void InitializeCore()
    {
        ID3D11Device? device = null;
        try
        {
            device = CreateDevice(out string driverKind);
            (IDXGIOutput1 output1, int width, int height) = GetPrimaryOutput(device);
            using (output1)
            {
                IDXGIOutputDuplication duplication = output1.DuplicateOutput(device);
                _duplication = duplication;
                _firstFrameAfterInit = true;
                _desktopWidth = width;
                _desktopHeight = height;
                _device = device;
                DriverKind = driverKind;
                device = null;
                _log.LogInformation("DXGI duplication initialized on {Width}x{Height} via {Driver}.", width, height, driverKind);
            }
        }
        finally
        {
            device?.Dispose();
        }
    }

    private static ID3D11Device CreateDevice(out string driverKind)
    {
        FeatureLevel[] levels =
        [
            FeatureLevel.Level_12_1,
            FeatureLevel.Level_12_0,
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
        ];

        foreach (DriverType driver in new[] { DriverType.Hardware, DriverType.Warp })
        {
            try
            {
                ID3D11Device device = D3D11.D3D11CreateDevice(driver, DeviceCreationFlags.BgraSupport, levels);
                driverKind = driver == DriverType.Hardware ? "Hardware" : "WARP";
                return device;
            }
            catch (Exception ex) when (driver == DriverType.Hardware)
            {
                System.Diagnostics.Debug.WriteLine($"Hardware D3D11 device creation failed, trying WARP: {ex.Message}");
            }
        }

        throw new InvalidOperationException("Failed to create a D3D11 device (hardware and WARP).");
    }

    private static (IDXGIOutput1 Output1, int Width, int Height) GetPrimaryOutput(ID3D11Device device)
    {
        using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
        Result enumResult = adapter.EnumOutputs(0, out IDXGIOutput? output);
        enumResult.CheckError();
        using (output)
        {
            if (output is null)
            {
                throw new InvalidOperationException("No DXGI outputs found.");
            }

            OutputDescription desc = output.Description;
            int width = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left;
            int height = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top;
            IDXGIOutput1 output1 = output.QueryInterface<IDXGIOutput1>();
            return (output1, width, height);
        }
    }

    private void ReinitializeDuplication()
    {
        lock (_sync)
        {
            _staging?.Dispose();
            _staging = null;
            _duplication?.Dispose();
            _duplication = null;
            try
            {
                if (_device is null)
                {
                    InitializeCore();
                    return;
                }

                (IDXGIOutput1 output1, int width, int height) = GetPrimaryOutput(_device);
                using (output1)
                {
                    _duplication = output1.DuplicateOutput(_device);
                    _firstFrameAfterInit = true;
                    _desktopWidth = width;
                    _desktopHeight = height;
                }

                _log.LogInformation("DXGI duplication reinitialized after access loss.");
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to reinitialize DXGI duplication; retrying on next frame.");
            }
        }
    }
}
