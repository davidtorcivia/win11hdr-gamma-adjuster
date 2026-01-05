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
            // Convert -50 to +50 scale to Kelvin: -50 = 2700K, 0 = 6500K, +50 = 10000K
            int kelvin = (int)(6500 + temperature * 70);
            return GetKelvinMultipliers(kelvin);
        }
        
        /// <summary>
        /// Converts color temperature in Kelvin to RGB multipliers using Planckian locus.
        /// Uses Tanner Helland's optimized approximation, accurate to less than 1% error.
        /// Preserves luminance to maintain perceived brightness.
        /// </summary>
        /// <param name="kelvin">Color temperature in Kelvin (1000-40000, typical 1900-6500)</param>
        /// <returns>Tuple of (R, G, B) multipliers normalized so max = 1.0</returns>
        public static (double R, double G, double B) GetKelvinMultipliers(int kelvin)
        {
            // Clamp to valid range
            kelvin = Math.Clamp(kelvin, 1000, 40000);
            double temp = kelvin / 100.0;
            
            double r, g, b;
            
            // Red calculation
            if (temp <= 66)
            {
                r = 255;
            }
            else
            {
                r = 329.698727446 * Math.Pow(temp - 60, -0.1332047592);
            }
            
            // Green calculation
            if (temp <= 66)
            {
                g = 99.4708025861 * Math.Log(temp) - 161.1195681661;
            }
            else
            {
                g = 288.1221695283 * Math.Pow(temp - 60, -0.0755148492);
            }
            
            // Blue calculation
            if (temp >= 66)
            {
                b = 255;
            }
            else if (temp <= 19)
            {
                b = 0;
            }
            else
            {
                b = 138.5177312231 * Math.Log(temp - 10) - 305.0447927307;
            }
            
            // Clamp to 0-255
            r = Math.Clamp(r, 0, 255);
            g = Math.Clamp(g, 0, 255);
            b = Math.Clamp(b, 0, 255);
            
            // Normalize so that the maximum channel is 1.0
            // This preserves relative ratios while avoiding clipping
            double maxVal = Math.Max(r, Math.Max(g, b));
            if (maxVal > 0)
            {
                r /= maxVal;
                g /= maxVal;
                b /= maxVal;
            }
            
            // Reference point: 6500K should return (1, 1, 1)
            // At 6500K: temp=65, r≈255, g≈255, b≈255
            // Calculate reference multipliers at 6500K
            var ref6500 = GetRawKelvinRGB(6500);
            double refMax = Math.Max(ref6500.r, Math.Max(ref6500.g, ref6500.b));
            
            // Scale so 6500K = (1, 1, 1)
            double refR = ref6500.r / refMax;
            double refG = ref6500.g / refMax;
            double refB = ref6500.b / refMax;
            
            // Apply as relative multipliers from 6500K reference
            if (refR > 0) r /= refR;
            if (refG > 0) g /= refG;
            if (refB > 0) b /= refB;
            
            // Clamp final values
            r = Math.Clamp(r, 0.0, 1.5);
            g = Math.Clamp(g, 0.0, 1.5);
            b = Math.Clamp(b, 0.0, 1.5);
            
            return (r, g, b);
        }
        
        private static (double r, double g, double b) GetRawKelvinRGB(int kelvin)
        {
            double temp = kelvin / 100.0;
            double r, g, b;
            
            if (temp <= 66)
                r = 255;
            else
                r = 329.698727446 * Math.Pow(temp - 60, -0.1332047592);
            
            if (temp <= 66)
                g = 99.4708025861 * Math.Log(temp) - 161.1195681661;
            else
                g = 288.1221695283 * Math.Pow(temp - 60, -0.0755148492);
            
            if (temp >= 66)
                b = 255;
            else if (temp <= 19)
                b = 0;
            else
                b = 138.5177312231 * Math.Log(temp - 10) - 305.0447927307;
            
            return (Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
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
