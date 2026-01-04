using System;

namespace HDRGammaController.Core
{
    /// <summary>
    /// Color adjustment functions for temperature, tint, dimming, and RGB corrections.
    /// </summary>
    public static class ColorAdjustments
    {
        /// <summary>
        /// Applies perceptually-accurate dimming that preserves black levels.
        /// Uses power-law compression instead of linear multiply.
        /// </summary>
        /// <param name="value">Input value (0-1)</param>
        /// <param name="brightnessPercent">Brightness percentage (10-100)</param>
        /// <returns>Dimmed value preserving shadow detail</returns>
        public static double ApplyPerceptualDimming(double value, double brightnessPercent)
        {
            if (brightnessPercent >= 100.0) return value;
            if (brightnessPercent <= 0) return 0;
            
            // Clamp to valid range
            double brightness = Math.Clamp(brightnessPercent, 10.0, 100.0) / 100.0;
            
            // Power-law compression: raises shadow detail while compressing highlights
            // This preserves near-black tones better than linear multiply
            // Formula: output = input^(1/gamma_boost) * brightness
            // where gamma_boost increases as brightness decreases
            double gammaBoost = 1.0 + (1.0 - brightness) * 0.3; // 1.0 at 100%, 1.27 at 10%
            
            return Math.Pow(value, 1.0 / gammaBoost) * brightness;
        }
        
        /// <summary>
        /// Calculates RGB multipliers for a given color temperature shift.
        /// Uses simplified Planckian locus approximation.
        /// </summary>
        /// <param name="temperature">Temperature shift (-50 to +50). Negative = warmer, Positive = cooler</param>
        /// <returns>Tuple of (R, G, B) multipliers</returns>
        public static (double R, double G, double B) GetTemperatureMultipliers(double temperature)
        {
            if (Math.Abs(temperature) < 0.01) return (1.0, 1.0, 1.0);
            
            // Normalize to -1 to +1 range
            double t = Math.Clamp(temperature, -50.0, 50.0) / 50.0;
            
            // Warm (negative t): boost red, reduce blue
            // Cool (positive t): boost blue, reduce red
            double r, g, b;
            
            if (t < 0) // Warmer
            {
                // More red/yellow, less blue
                r = 1.0 + (-t) * 0.15;  // Up to 1.15
                g = 1.0 + (-t) * 0.05;  // Slight green boost for warmth
                b = 1.0 - (-t) * 0.25;  // Down to 0.75
            }
            else // Cooler
            {
                // More blue, less red
                r = 1.0 - t * 0.20;     // Down to 0.80
                g = 1.0 - t * 0.05;     // Slight green reduction
                b = 1.0 + t * 0.15;     // Up to 1.15
            }
            
            return (r, g, b);
        }
        
        /// <summary>
        /// Calculates RGB multipliers for tint adjustment (green/magenta axis).
        /// </summary>
        /// <param name="tint">Tint shift (-50 to +50). Negative = green, Positive = magenta</param>
        /// <returns>Tuple of (R, G, B) multipliers</returns>
        public static (double R, double G, double B) GetTintMultipliers(double tint)
        {
            if (Math.Abs(tint) < 0.01) return (1.0, 1.0, 1.0);
            
            // Normalize to -1 to +1 range
            double t = Math.Clamp(tint, -50.0, 50.0) / 50.0;
            
            double r, g, b;
            
            if (t < 0) // More green
            {
                r = 1.0 - (-t) * 0.08;
                g = 1.0 + (-t) * 0.10;
                b = 1.0 - (-t) * 0.08;
            }
            else // More magenta
            {
                r = 1.0 + t * 0.08;
                g = 1.0 - t * 0.12;
                b = 1.0 + t * 0.08;
            }
            
            return (r, g, b);
        }
        
        /// <summary>
        /// Applies all calibration adjustments to an RGB triplet.
        /// </summary>
        /// <param name="r">Red value (0-1)</param>
        /// <param name="g">Green value (0-1)</param>
        /// <param name="b">Blue value (0-1)</param>
        /// <param name="settings">Calibration settings to apply</param>
        /// <returns>Adjusted RGB values</returns>
        public static (double R, double G, double B) ApplyCalibration(
            double r, double g, double b, 
            CalibrationSettings settings)
        {
            // 1. Apply perceptual dimming
            if (settings.Brightness < 100.0)
            {
                r = ApplyPerceptualDimming(r, settings.Brightness);
                g = ApplyPerceptualDimming(g, settings.Brightness);
                b = ApplyPerceptualDimming(b, settings.Brightness);
            }
            
            // 2. Apply temperature
            if (Math.Abs(settings.Temperature) > 0.01)
            {
                var temp = GetTemperatureMultipliers(settings.Temperature);
                r *= temp.R;
                g *= temp.G;
                b *= temp.B;
            }
            
            // 3. Apply tint
            if (Math.Abs(settings.Tint) > 0.01)
            {
                var tint = GetTintMultipliers(settings.Tint);
                r *= tint.R;
                g *= tint.G;
                b *= tint.B;
            }
            
            // 4. Apply RGB gains
            r *= settings.RedGain;
            g *= settings.GreenGain;
            b *= settings.BlueGain;
            
            // 5. Apply RGB offsets (lift)
            r += settings.RedOffset;
            g += settings.GreenOffset;
            b += settings.BlueOffset;
            
            // 6. Clamp to valid range
            r = Math.Clamp(r, 0.0, 1.0);
            g = Math.Clamp(g, 0.0, 1.0);
            b = Math.Clamp(b, 0.0, 1.0);
            
            return (r, g, b);
        }
    }
}
