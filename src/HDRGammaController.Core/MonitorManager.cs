using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using HDRGammaController.Interop;

namespace HDRGammaController.Core
{
    public class MonitorManager
    {
        public List<MonitorInfo> EnumerateMonitors()
        {
            var monitors = new List<MonitorInfo>();
            
            // 1. Create DXGI Factory1 (Base for newer)
            Console.WriteLine("MonitorManager: Creating DXGI Factory1...");
            Guid iidFactory1 = Dxgi.IID_IDXGIFactory1;
            int hr = Dxgi.CreateDXGIFactory1(ref iidFactory1, out IntPtr pFactory);
            Console.WriteLine($"MonitorManager: CreateDXGIFactory1 returned HR={hr}, Ptr={pFactory}");

            if (pFactory == IntPtr.Zero)
            {
                // Fallback or error
                Console.WriteLine("MonitorManager: Failed to create factory.");
                return monitors;
            }

            // We created an IDXGIFactory1.
            Dxgi.IDXGIFactory1? factory = null;
            try {
                 object? factoryObj = Marshal.GetObjectForIUnknown(pFactory);
                 factory = factoryObj as Dxgi.IDXGIFactory1;
            } catch (Exception ex) {
                Console.WriteLine($"MonitorManager: Failed to wrap IDXGIFactory1: {ex.Message}");
            }
            
            if (factory == null)
            {
                Console.WriteLine("MonitorManager: IDXGIFactory1 wrapper failed.");
                Marshal.Release(pFactory);
                return monitors;
            }

            try
            {
                uint adapterIndex = 0;

                // 2. Enum Adapters
                while (true)
                {
                    IntPtr pAdapter = IntPtr.Zero;
                    hr = factory.EnumAdapters1(adapterIndex, out pAdapter);
                    
                    if (hr == -2147024896) // DXGI_ERROR_NOT_FOUND (End of enumeration)
                    {
                         break;
                    }
                    if (hr < 0)
                    {
                        Console.WriteLine($"MonitorManager: EnumAdapters1 failed for {adapterIndex} with HR={hr}");
                        break;
                    }

                    if (pAdapter == IntPtr.Zero) break;
                    
                    // Wrap adapter using GetObjectForIUnknown and cast via interface
                    Dxgi.IDXGIAdapter1? adapter = null;
                    try {
                        object? adapterObj = Marshal.GetObjectForIUnknown(pAdapter);
                        adapter = adapterObj as Dxgi.IDXGIAdapter1;
                        if (adapter == null && adapterObj != null)
                        {
                            Console.WriteLine($"MonitorManager: Adapter {adapterIndex} doesn't support IDXGIAdapter1, type: {adapterObj.GetType().Name}");
                        }
                    } catch (Exception ex) {
                        Console.WriteLine($"MonitorManager: Exception wrapping adapter {adapterIndex}: {ex.GetType().Name}: {ex.Message}");
                    }
                    
                    if (adapter == null) {
                         Console.WriteLine($"MonitorManager: Failed to wrap IDXGIAdapter1 for index {adapterIndex}");
                         Marshal.Release(pAdapter);
                         adapterIndex++;
                         continue;
                    }

                    Console.WriteLine($"MonitorManager: Found adapter at index {adapterIndex}");

                    Dxgi.DXGI_ADAPTER_DESC1 adapterDesc;
                    adapter.GetDesc1(out adapterDesc);

                    uint outputIndex = 0;

                    // 3. Enum Outputs
                    while (true)
                    {
                        IntPtr pOutput = IntPtr.Zero;
                        hr = adapter.EnumOutputs(outputIndex, out pOutput);
                        
                        if (hr == -2147024896) // DXGI_ERROR_NOT_FOUND
                        {
                            break;
                        }
                        if (hr < 0)
                        {
                            Console.WriteLine($"MonitorManager: EnumOutputs failed for {outputIndex} with HR={hr}");
                            break;
                        }

                        Console.WriteLine($"MonitorManager: Found output {outputIndex}");

                        // QueryInterface for Output6 (HDR)
                        Dxgi.IDXGIOutput6? output6 = null;
                        try {
                            // If we have pOutput, we can get object
                            object? outObj = Marshal.GetObjectForIUnknown(pOutput);
                            output6 = outObj as Dxgi.IDXGIOutput6;
                        } catch {}

                        if (output6 != null)
                        {
                            Dxgi.DXGI_OUTPUT_DESC1 desc1;
                            output6.GetDesc1(out desc1);

                            var monitorInfo = new MonitorInfo
                            {
                                DeviceName = desc1.DeviceName,
                                AdapterLuid = adapterDesc.AdapterLuid,
                                OutputId = outputIndex,
                                HMonitor = desc1.Monitor,
                                IsHdrCapable = (desc1.ColorSpace == Dxgi.DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020),
                                IsHdrActive = (desc1.ColorSpace == Dxgi.DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020)
                            };

                            EnrichWithGdiData(monitorInfo);
                            monitors.Add(monitorInfo);
                        }
                        else
                        {
                            Console.WriteLine($"MonitorManager: Output {outputIndex} does not support IDXGIOutput6.");
                        }

                        if (pOutput != IntPtr.Zero)
                        {
                             Marshal.Release(pOutput);
                        }

                        outputIndex++;
                    }

                    // Done with Adapter
                    Marshal.Release(pAdapter);
                    adapterIndex++;
                }
            }
            finally
            {
                if (factory != null) Marshal.ReleaseComObject(factory);
            }

            return monitors;
        }

        private void EnrichWithGdiData(MonitorInfo monitor)
        {
            // Use EnumDisplayDevices to find friendly name and path
            // Match via monitor.DeviceName (\\.\DISPLAY1)
            
            var displayDevice = new User32.DISPLAY_DEVICE();
            displayDevice.Initialize();

            if (User32.EnumDisplayDevices(monitor.DeviceName, 0, ref displayDevice, 0))
            {
                // This gets the Monitor info attached to the adapter output
                // displayDevice.DeviceID looks like: MONITOR\GSM5B08\{GUID}
                monitor.MonitorDevicePath = displayDevice.DeviceID;
                monitor.FriendlyName = displayDevice.DeviceString;
            }
            else
            {
                // Try enumerating adapter first
                var adapterDevice = new User32.DISPLAY_DEVICE();
                adapterDevice.Initialize();
                
                // We access the device by name, but EnumDisplayDevices expects lpDevice to be NULL for enum all, or name for specific.
                // If we pass monitor.DeviceName, we get the Adapter info usually?
                // Wait. EnumDisplayDevices(monitor.DeviceName, 0...) gets the first monitor attached to that display output.
                
                // Standard flow:
                // 1. EnumDisplayDevices(NULL, i) -> Adapter (\\.\DISPLAY1)
                // 2. EnumDisplayDevices(Adapter.DeviceName, 0) -> Monitor (MONITOR\...)
                
                // Since we already have DeviceName from DXGI (\\.\DISPLAY1), we can just call Step 2.
                // Which we did above.
            }
        }
    }
}
