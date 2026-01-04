using HDRGammaController.Interop;

namespace HDRGammaController.Core
{
    public class MonitorInfo
    {
        /// <summary>
        /// GDI Device Name (e.g. \\.\DISPLAY1).
        /// </summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// Friendly name from EDID/GDI (e.g. "LG OLED TV").
        /// </summary>
        public string FriendlyName { get; set; } = string.Empty;

        /// <summary>
        /// ID of the specific monitor instance (e.g. \\?\DISPLAY#GSM5B08#...).
        /// Used for persistence.
        /// </summary>
        public string MonitorDevicePath { get; set; } = string.Empty;

        public bool IsHdrCapable { get; set; }
        
        /// <summary>
        /// True if the OS is currently outputting HDR (ST.2084).
        /// </summary>
        public bool IsHdrActive { get; set; }

        public GammaMode CurrentGamma { get; set; } = GammaMode.Gamma24;
        
        /// <summary>
        /// SDR White Level in nits (from Windows settings or manual override).
        /// </summary>
        public double SdrWhiteLevel { get; set; } = 200.0;

        // DXGI details
        public Dxgi.LUID AdapterLuid { get; set; }
        public uint OutputId { get; set; } // Index in EnumOutputs
        public IntPtr HMonitor { get; set; }
    }
}
