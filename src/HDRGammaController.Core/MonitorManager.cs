using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
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
                        try
                        {
                            // If we have pOutput, we can get object
                            object? outObj = Marshal.GetObjectForIUnknown(pOutput);
                            output6 = outObj as Dxgi.IDXGIOutput6;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"MonitorManager: Failed to get IDXGIOutput6 for output {outputIndex}: {ex.Message}");
                        }

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
                                IsHdrActive = (desc1.ColorSpace == Dxgi.DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020),
                                MonitorBounds = desc1.DesktopCoordinates
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
                
                // Try to get the real monitor name from EDID in registry
                string? edidName = GetMonitorNameFromEdid(displayDevice.DeviceID);
                if (!string.IsNullOrEmpty(edidName))
                {
                    monitor.FriendlyName = edidName;
                    Console.WriteLine($"MonitorManager: Got EDID name '{edidName}' for {monitor.DeviceName}");
                }
                else
                {
                    // Fallback to GDI name (usually "Generic PnP Monitor")
                    monitor.FriendlyName = displayDevice.DeviceString;
                }
            }
        }
        
        /// <summary>
        /// Attempts to read the monitor's friendly name from the EDID data in the registry.
        /// </summary>
        private string? GetMonitorNameFromEdid(string deviceId)
        {
            try
            {
                // DeviceID format: MONITOR\{ManufacturerID}{ProductCode}\{InstanceID}
                // e.g., MONITOR\GSM5B08\{4d36e96e-e325-11ce-bfc1-08002be10318}\0008
                // We need to find the corresponding registry key under:
                // HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\{ManufacturerID}{ProductCode}\{InstanceID}\Device Parameters\EDID
                
                if (string.IsNullOrEmpty(deviceId)) return null;
                
                // Parse the device ID to extract manufacturer and product code
                string[] parts = deviceId.Split('\\');
                if (parts.Length < 2) return null;
                
                string monitorId = parts[1]; // e.g., "GSM5B08"
                
                // Search in DISPLAY registry for matching monitors
                using (var displayKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY"))
                {
                    if (displayKey == null) return null;
                    
                    foreach (string subKeyName in displayKey.GetSubKeyNames())
                    {
                        if (!subKeyName.Equals(monitorId, StringComparison.OrdinalIgnoreCase)) continue;
                        
                        using (var monitorKey = displayKey.OpenSubKey(subKeyName))
                        {
                            if (monitorKey == null) continue;
                            
                            foreach (string instanceName in monitorKey.GetSubKeyNames())
                            {
                                using (var instanceKey = monitorKey.OpenSubKey(instanceName))
                                {
                                    if (instanceKey == null) continue;
                                    
                                    using (var paramsKey = instanceKey.OpenSubKey("Device Parameters"))
                                    {
                                        if (paramsKey == null) continue;
                                        
                                        byte[]? edid = paramsKey.GetValue("EDID") as byte[];
                                        if (edid != null && edid.Length > 0)
                                        {
                                            string? name = ParseEdidForName(edid);
                                            if (!string.IsNullOrEmpty(name))
                                            {
                                                return name;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MonitorManager: Error reading EDID: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Parses EDID data to extract the monitor name from descriptor blocks.
        /// </summary>
        private string? ParseEdidForName(byte[] edid)
        {
            // EDID is 128 bytes minimum
            // Detailed timing descriptors start at byte 54
            // Each descriptor is 18 bytes
            // Descriptor type 0xFC = Monitor name

            // SECURITY: Validate minimum EDID size before any access
            if (edid == null || edid.Length < 128) return null;

            // Check for valid EDID header
            if (edid[0] != 0x00 || edid[1] != 0xFF || edid[2] != 0xFF || edid[3] != 0xFF ||
                edid[4] != 0xFF || edid[5] != 0xFF || edid[6] != 0xFF || edid[7] != 0x00)
            {
                return null;
            }

            // Search through the 4 descriptor blocks (starting at offsets 54, 72, 90, 108)
            for (int offset = 54; offset <= 108; offset += 18)
            {
                // SECURITY: Bounds check - ensure we won't read past end of array
                // We need to access offset+5 through offset+17 (13 bytes of name data)
                if (offset + 18 > edid.Length)
                {
                    Console.WriteLine($"MonitorManager: EDID too short for descriptor at offset {offset}");
                    break;
                }

                // Check if this is a text descriptor (bytes 0-3 are 0x00, byte 3 is descriptor type)
                if (edid[offset] == 0x00 && edid[offset + 1] == 0x00 &&
                    edid[offset + 2] == 0x00 && edid[offset + 3] == 0xFC)
                {
                    // Bytes 5-17 contain the monitor name (13 characters max)
                    // SECURITY: Calculate safe copy length
                    int nameStartOffset = offset + 5;
                    int maxCopyLen = Math.Min(13, edid.Length - nameStartOffset);
                    if (maxCopyLen <= 0) continue;

                    var nameBytes = new byte[maxCopyLen];
                    Array.Copy(edid, nameStartOffset, nameBytes, 0, maxCopyLen);

                    string name = Encoding.ASCII.GetString(nameBytes);
                    // Trim trailing newlines, spaces, and null characters
                    name = name.TrimEnd('\n', '\r', ' ', '\0');

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
            }

            return null;
        }
    }
}
