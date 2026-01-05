namespace HDRGammaController.Core
{
    /// <summary>
    /// Per-monitor calibration settings for advanced adjustments.
    /// </summary>
    public class CalibrationSettings
    {
        /// <summary>
        /// Brightness level (10-100%). Uses perceptual compression to preserve shadows.
        /// </summary>
        public double Brightness { get; set; } = 100.0;
        
        /// <summary>
        /// Color temperature adjustment (-50 to +50).
        /// Negative = warmer (more red/yellow), Positive = cooler (more blue).
        /// </summary>
        public double Temperature { get; set; } = 0.0;
        
        /// <summary>
        /// Tint adjustment (-50 to +50).
        /// Negative = more green, Positive = more magenta.
        /// </summary>
        public double Tint { get; set; } = 0.0;
        
        /// <summary>
        /// Red channel gain multiplier (0.5 to 1.5).
        /// </summary>
        public double RedGain { get; set; } = 1.0;
        
        /// <summary>
        /// Green channel gain multiplier (0.5 to 1.5).
        /// </summary>
        public double GreenGain { get; set; } = 1.0;
        
        /// <summary>
        /// Blue channel gain multiplier (0.5 to 1.5).
        /// </summary>
        public double BlueGain { get; set; } = 1.0;
        
        /// <summary>
        /// Red channel offset/lift (-0.1 to +0.1).
        /// </summary>
        public double RedOffset { get; set; } = 0.0;
        
        /// <summary>
        /// Green channel offset/lift (-0.1 to +0.1).
        /// </summary>
        public double GreenOffset { get; set; } = 0.0;
        
        /// <summary>
        /// Blue channel offset/lift (-0.1 to +0.1).
        /// </summary>
        public double BlueOffset { get; set; } = 0.0;
        
        /// <summary>
        /// Algorithm to use for temperature adjustment.
        /// </summary>
        /// <summary>
        /// Algorithm to use for temperature adjustment.
        /// </summary>
        public NightModeAlgorithm Algorithm { get; set; } = NightModeAlgorithm.Standard;

        /// <summary>
        /// If true, uses standard linear dimming instead of perceptual (gamma-lift) dimming.
        /// </summary>
        public bool UseLinearBrightness { get; set; } = false;

        /// <summary>
        /// Returns true if any adjustments are applied (non-default values).
        /// </summary>
        public bool HasAdjustments =>
            Math.Abs(Brightness - 100.0) > 0.01 ||
            Math.Abs(Temperature) > 0.01 ||
            Math.Abs(Tint) > 0.01 ||
            Math.Abs(RedGain - 1.0) > 0.001 ||
            Math.Abs(GreenGain - 1.0) > 0.001 ||
            Math.Abs(BlueGain - 1.0) > 0.001 ||
            Math.Abs(RedOffset) > 0.001 ||
            Math.Abs(GreenOffset) > 0.001 ||
            Math.Abs(BlueOffset) > 0.001;
        
        /// <summary>
        /// Creates a default (no adjustment) calibration.
        /// </summary>
        public static CalibrationSettings Default => new CalibrationSettings();
        
        /// <summary>
        /// Creates a copy of this settings object.
        /// </summary>
        public CalibrationSettings Clone() => new CalibrationSettings
        {
            Brightness = this.Brightness,
            UseLinearBrightness = this.UseLinearBrightness,
            Temperature = this.Temperature,
            Tint = this.Tint,
            RedGain = this.RedGain,
            GreenGain = this.GreenGain,
            BlueGain = this.BlueGain,
            RedOffset = this.RedOffset,
            GreenOffset = this.GreenOffset,
            BlueOffset = this.BlueOffset,
            Algorithm = this.Algorithm
        };
    }
}
