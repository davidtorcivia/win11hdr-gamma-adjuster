using System;
using System.Collections.Generic;
using System.Linq;
using HDRGammaController.Interop;

namespace HDRGammaController.Core
{
    public static class MonitorCorrelator
    {
        // For now, we use a simple correlation based on Device Name (e.g. \\.\DISPLAY1).
        // DXGI Output GetDesc() returns DeviceName which matches GDI EnumDisplayDevices.
        
        public static string? GetGdiDeviceNameFromDxgi(IntPtr pOutput)
        {
            try
            {
                // We don't have GetDesc on IntPtr obviously. 
                // We need to wrap it or use the interface definitions we made.
                // But wait, the interface definitions are interfaces, we need an object instance to call them on.
                // Marshal.GetObjectForIUnknown(pOutput) -> object -> cast to Interface.
                
                object? outObj = System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(pOutput);
                if (outObj is Dxgi.IDXGIOutput6 output6)
                {
                    Dxgi.DXGI_OUTPUT_DESC1 desc1;
                    output6.GetDesc1(out desc1);
                    return desc1.DeviceName;
                }
                
                // If we can't get Output6, we can try to cast to a manually defined IDXGIOutput if we restored it, 
                // OR we can just fail for now as we target Win10+ HDR anyway.
            }
            catch
            {
                // ignore
            }
            return null;
        }
    }
}
