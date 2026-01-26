using System;
using System.Collections.Concurrent;
using System.Linq;
using HDRGammaController.Core.Calibration;

namespace HDRGammaController.Core
{
    /// <summary>
    /// Generates 1D lookup tables for HDR/SDR gamma correction with optional calibration.
    /// </summary>
    /// <remarks>
    /// The LUT generator implements a multi-stage pipeline:
    /// 1. PQ EOTF decode (signal to linear nits)
    /// 2. Normalize to SDR range
    /// 3. Apply target gamma curve (2.2 or 2.4)
    /// 4. Apply calibration adjustments in linear space
    /// 5. PQ OETF encode (linear nits to signal)
    /// 6. Blend toward passthrough in HDR headroom region
    ///
    /// LUT results are cached to avoid redundant computation for identical parameters.
    /// </remarks>
    public static class LutGenerator
    {
        // Cache for computed LUTs to avoid redundant computation
        // Key: (GammaMode, WhiteLevel rounded to nearest 10, CalibrationHash, IsHdr)
        private static readonly ConcurrentDictionary<(GammaMode, int, int, bool), (double[], double[], double[], double[])> _lutCache = new();

        // Maximum cache size to prevent unbounded memory growth
        private const int MaxCacheSize = 50;

        /// <summary>
        /// Generates a 1024-point 1D LUT for HDR gamma correction (single channel, no calibration).
        /// </summary>
        public static double[] GenerateLut(GammaMode gammaMode, double sdrWhiteLevel, bool isHdr = true)
        {
            return GenerateLut(gammaMode, sdrWhiteLevel, CalibrationSettings.Default, isHdr).Grey;
        }

        /// <summary>
        /// Clears the LUT cache to free memory.
        /// </summary>
        public static void ClearCache()
        {
            _lutCache.Clear();
        }
        
        /// <summary>
        /// Generates per-channel 1024-point 1D LUTs for HDR gamma correction with calibration.
        /// Results are cached for performance when called with identical parameters.
        /// </summary>
        /// <param name="gammaMode">The target gamma curve (2.2 or 2.4).</param>
        /// <param name="sdrWhiteLevel">The SDR white level in nits (e.g. 80, 200, 480).</param>
        /// <param name="calibration">Calibration settings for dimming, temp, tint, RGB.</param>
        /// <param name="isHdr">Whether the target display is in HDR mode.</param>
        /// <returns>Per-channel LUTs (R, G, B) and a Grey reference.</returns>
        public static (double[] R, double[] G, double[] B, double[] Grey) GenerateLut(
            GammaMode gammaMode,
            double sdrWhiteLevel,
            CalibrationSettings calibration,
            bool isHdr = true)
        {
            // Create cache key (round white level to reduce cache fragmentation)
            int whiteLevelKey = (int)Math.Round(sdrWhiteLevel / 10.0) * 10;
            int calibrationHash = calibration.GetHashCode();
            var cacheKey = (gammaMode, whiteLevelKey, calibrationHash, isHdr);

            // Try to get from cache
            if (_lutCache.TryGetValue(cacheKey, out var cachedLut))
            {
                // Return copies to prevent modification of cached data
                return (
                    (double[])cachedLut.Item1.Clone(),
                    (double[])cachedLut.Item2.Clone(),
                    (double[])cachedLut.Item3.Clone(),
                    (double[])cachedLut.Item4.Clone()
                );
            }

            // Generate new LUT
            var result = GenerateLutInternal(gammaMode, sdrWhiteLevel, calibration, isHdr);

            // Add to cache (evict oldest entries if cache is full)
            if (_lutCache.Count >= MaxCacheSize)
            {
                // Simple eviction: clear half the cache when full
                // A more sophisticated LRU could be implemented if needed
                var keysToRemove = _lutCache.Keys.Take(MaxCacheSize / 2).ToArray();
                foreach (var key in keysToRemove)
                {
                    _lutCache.TryRemove(key, out _);
                }
            }

            _lutCache.TryAdd(cacheKey, result);

            // Return copies
            return (
                (double[])result.Item1.Clone(),
                (double[])result.Item2.Clone(),
                (double[])result.Item3.Clone(),
                (double[])result.Item4.Clone()
            );
        }

        /// <summary>
        /// Internal LUT generation without caching.
        /// </summary>
        private static (double[] R, double[] G, double[] B, double[] Grey) GenerateLutInternal(
            GammaMode gammaMode,
            double sdrWhiteLevel,
            CalibrationSettings calibration,
            bool isHdr)
        {
            double[] lutR = new double[1024];
            double[] lutG = new double[1024];
            double[] lutB = new double[1024];
            double[] lutGrey = new double[1024];

            if (gammaMode == GammaMode.WindowsDefault && !calibration.HasAdjustments)
            {
                // Identity LUT
                for (int i = 0; i < 1024; i++)
                {
                    double val = i / 1023.0;
                    lutR[i] = val;
                    lutG[i] = val;
                    lutB[i] = val;
                    lutGrey[i] = val;
                }
                return (lutR, lutG, lutB, lutGrey);
            }

            if (!isHdr)
            {
                // SDR Generation logic: Gamma 2.2 Decode -> Calibrate -> Gamma 2.2 Encode
                // This ensures linear operations (like tint/temp) are perceptually uniform
                for (int i = 0; i < 1024; i++)
                {
                    double input = i / 1023.0;
                    
                    // 1. Decode generic Gamma 2.2 (Standard Windows SDR)
                    // We assume the signal is sRGB/Gamma 2.2
                    double linear = Math.Pow(input, 2.2);
                    
                    // 2. Apply Calibration
                    var (r, g, b) = ColorAdjustments.ApplyCalibration(linear, linear, linear, calibration);
                    
                    // 3. Encode back to Gamma 2.2
                    lutR[i] = Math.Pow(r, 1.0 / 2.2);
                    lutG[i] = Math.Pow(g, 1.0 / 2.2);
                    lutB[i] = Math.Pow(b, 1.0 / 2.2);
                    lutGrey[i] = (lutR[i] + lutG[i] + lutB[i]) / 3.0;
                }
                return (lutR, lutG, lutB, lutGrey);
            }

            double gamma = gammaMode switch
            {
                GammaMode.Gamma24 => 2.4,
                GammaMode.Gamma22 => 2.2,
                _ => 1.0 // WindowsDefault with calibration
            };
            
            double blackLevel = 0.0;

            for (int i = 0; i < 1024; i++)
            {
                double normalized = i / 1023.0;

                // 1. PQ EOTF -> linear nits
                double linear = TransferFunctions.PqEotf(normalized);

                double outputR, outputG, outputB;
                
                if (gammaMode == GammaMode.WindowsDefault)
                {
                    // No gamma correction, just apply calibration
                    outputR = outputG = outputB = linear;
                }
                else
                {
                    // 2. Input Light -> Simulated Signal (inverse sRGB)
                    double srgbNormalized = TransferFunctions.SrgbInverseEotf(linear, sdrWhiteLevel, blackLevel);

                    // 3. Apply gamma
                    double gammaApplied = Math.Pow(srgbNormalized, gamma);

                    // 4. Scale to output nits
                    double outputLinear = blackLevel + (sdrWhiteLevel - blackLevel) * gammaApplied;
                    
                    outputR = outputG = outputB = outputLinear;
                }

                // 5. Apply calibration adjustments (dimming, temp, tint, RGB)
                if (calibration.HasAdjustments)
                {
                    // Normalize to 0-1 range for calibration
                    double normR = Math.Clamp(outputR / sdrWhiteLevel, 0, 1);
                    double normG = Math.Clamp(outputG / sdrWhiteLevel, 0, 1);
                    double normB = Math.Clamp(outputB / sdrWhiteLevel, 0, 1);
                    
                    var (adjR, adjG, adjB) = ColorAdjustments.ApplyCalibration(normR, normG, normB, calibration);
                    
                    // Scale back to nits
                    outputR = adjR * sdrWhiteLevel;
                    outputG = adjG * sdrWhiteLevel;
                    outputB = adjB * sdrWhiteLevel;
                }

                // 6. Encode to PQ
                double pqR = TransferFunctions.PqInverseEotf(outputR);
                double pqG = TransferFunctions.PqInverseEotf(outputG);
                double pqB = TransferFunctions.PqInverseEotf(outputB);
                double pqGrey = TransferFunctions.PqInverseEotf((outputR + outputG + outputB) / 3.0);

                // 7. HDR Headroom Preservation
                // For content below SDR white level: apply full calibration
                // For HDR headroom (above SDR white): blend toward passthrough
                // This ensures bright HDR highlights aren't affected by calibration (e.g., warm temperature)
                // while SDR-range content gets the full calibration treatment.
                if (linear <= sdrWhiteLevel)
                {
                    lutR[i] = pqR;
                    lutG[i] = pqG;
                    lutB[i] = pqB;
                    lutGrey[i] = pqGrey;
                }
                else
                {
                    // Blend toward passthrough in HDR headroom
                    // At sdrWhiteLevel: 100% calibrated output
                    // At 10000 nits: 100% passthrough (original signal = normalized)
                    // This preserves HDR highlight detail and color accuracy from source content
                    double headroomRange = 10000.0 - sdrWhiteLevel;
                    double headroomPosition = (linear - sdrWhiteLevel) / headroomRange;
                    double blendFactor = Math.Min(1.0, headroomPosition);

                    lutR[i] = pqR + (normalized - pqR) * blendFactor;
                    lutG[i] = pqG + (normalized - pqG) * blendFactor;
                    lutB[i] = pqB + (normalized - pqB) * blendFactor;
                    lutGrey[i] = pqGrey + (normalized - pqGrey) * blendFactor;
                }
            }

            return (lutR, lutG, lutB, lutGrey);
        }

        #region Calibrated LUT Generation

        /// <summary>
        /// Generates per-channel 1D LUTs using measured display characteristics.
        /// This provides accurate gamma compensation based on actual colorimeter measurements.
        /// </summary>
        /// <param name="targetGamma">The desired output gamma (2.2, 2.4, etc.).</param>
        /// <param name="profile">The calibration profile with measured tone curves.</param>
        /// <param name="calibration">Additional calibration settings (temperature, tint, etc.).</param>
        /// <param name="sdrWhiteLevel">SDR white level in nits.</param>
        /// <param name="isHdr">Whether the display is in HDR mode.</param>
        /// <returns>Per-channel LUTs that compensate for the display's actual response.</returns>
        public static (double[] R, double[] G, double[] B, double[] Grey) GenerateCalibratedLut(
            double targetGamma,
            DisplayCalibrationProfile profile,
            CalibrationSettings calibration,
            double sdrWhiteLevel,
            bool isHdr = true)
        {
            // Convert profile to characterization
            var characterization = profile.ToCharacterization();
            return GenerateCalibratedLut(targetGamma, characterization, calibration, sdrWhiteLevel, isHdr);
        }

        /// <summary>
        /// Generates per-channel 1D LUTs using measured display characteristics.
        /// </summary>
        public static (double[] R, double[] G, double[] B, double[] Grey) GenerateCalibratedLut(
            double targetGamma,
            DisplayCharacterization characterization,
            CalibrationSettings calibration,
            double sdrWhiteLevel,
            bool isHdr = true)
        {
            double[] lutR = new double[1024];
            double[] lutG = new double[1024];
            double[] lutB = new double[1024];
            double[] lutGrey = new double[1024];

            // Get the measured tone curves (what the display actually does)
            var measuredR = characterization.RedToneCurve ?? ToneCurve.CreateGamma(characterization.MeasuredGamma);
            var measuredG = characterization.GreenToneCurve ?? ToneCurve.CreateGamma(characterization.MeasuredGamma);
            var measuredB = characterization.BlueToneCurve ?? ToneCurve.CreateGamma(characterization.MeasuredGamma);

            if (!isHdr)
            {
                // SDR Mode: Compute compensation curves
                // For each input signal level, find what signal to send to get the target output
                for (int i = 0; i < 1024; i++)
                {
                    double input = i / 1023.0;

                    // What linear light level do we WANT for this input?
                    // (Input represents the encoded signal, target gamma defines the desired decoding)
                    double targetLinear = Math.Pow(input, targetGamma);

                    // Apply calibration adjustments to the target
                    var (adjR, adjG, adjB) = ColorAdjustments.ApplyCalibration(
                        targetLinear, targetLinear, targetLinear, calibration);

                    // What signal must we send to the display to get this output?
                    // Use the INVERSE of the measured response
                    lutR[i] = measuredR.InverseLookup(Math.Clamp(adjR, 0, 1));
                    lutG[i] = measuredG.InverseLookup(Math.Clamp(adjG, 0, 1));
                    lutB[i] = measuredB.InverseLookup(Math.Clamp(adjB, 0, 1));
                    lutGrey[i] = (lutR[i] + lutG[i] + lutB[i]) / 3.0;
                }
                return (lutR, lutG, lutB, lutGrey);
            }

            // HDR Mode with calibration-aware compensation
            double blackLevel = characterization.BlackLevel;

            for (int i = 0; i < 1024; i++)
            {
                double normalized = i / 1023.0;

                // 1. PQ EOTF -> linear nits
                double linear = TransferFunctions.PqEotf(normalized);

                double outputR, outputG, outputB;

                if (Math.Abs(targetGamma - 1.0) < 0.01)
                {
                    // Linear mode (no gamma correction, just calibration)
                    outputR = outputG = outputB = linear;
                }
                else
                {
                    // 2. Compute what linear output we want based on target gamma
                    double srgbNormalized = TransferFunctions.SrgbInverseEotf(linear, sdrWhiteLevel, blackLevel);
                    double gammaApplied = Math.Pow(srgbNormalized, targetGamma);
                    double targetLinear = blackLevel + (sdrWhiteLevel - blackLevel) * gammaApplied;
                    outputR = outputG = outputB = targetLinear;
                }

                // 3. Apply calibration adjustments
                if (calibration.HasAdjustments)
                {
                    double normR = Math.Clamp(outputR / sdrWhiteLevel, 0, 1);
                    double normG = Math.Clamp(outputG / sdrWhiteLevel, 0, 1);
                    double normB = Math.Clamp(outputB / sdrWhiteLevel, 0, 1);

                    var (adjR, adjG, adjB) = ColorAdjustments.ApplyCalibration(normR, normG, normB, calibration);

                    outputR = adjR * sdrWhiteLevel;
                    outputG = adjG * sdrWhiteLevel;
                    outputB = adjB * sdrWhiteLevel;
                }

                // 4. Compensate for display's actual response
                // Convert target linear to the signal level that produces it on THIS display
                double targetNormR = Math.Clamp(outputR / sdrWhiteLevel, 0, 1);
                double targetNormG = Math.Clamp(outputG / sdrWhiteLevel, 0, 1);
                double targetNormB = Math.Clamp(outputB / sdrWhiteLevel, 0, 1);

                double compensatedR = measuredR.InverseLookup(targetNormR) * sdrWhiteLevel;
                double compensatedG = measuredG.InverseLookup(targetNormG) * sdrWhiteLevel;
                double compensatedB = measuredB.InverseLookup(targetNormB) * sdrWhiteLevel;

                // 5. Encode to PQ
                double pqR = TransferFunctions.PqInverseEotf(compensatedR);
                double pqG = TransferFunctions.PqInverseEotf(compensatedG);
                double pqB = TransferFunctions.PqInverseEotf(compensatedB);
                double pqGrey = TransferFunctions.PqInverseEotf((compensatedR + compensatedG + compensatedB) / 3.0);

                // 6. HDR Headroom Preservation
                if (linear <= sdrWhiteLevel)
                {
                    lutR[i] = pqR;
                    lutG[i] = pqG;
                    lutB[i] = pqB;
                    lutGrey[i] = pqGrey;
                }
                else
                {
                    // Blend toward passthrough in HDR headroom
                    double headroomRange = 10000.0 - sdrWhiteLevel;
                    double headroomPosition = (linear - sdrWhiteLevel) / headroomRange;
                    double blendFactor = Math.Min(1.0, headroomPosition);

                    lutR[i] = pqR + (normalized - pqR) * blendFactor;
                    lutG[i] = pqG + (normalized - pqG) * blendFactor;
                    lutB[i] = pqB + (normalized - pqB) * blendFactor;
                    lutGrey[i] = pqGrey + (normalized - pqGrey) * blendFactor;
                }
            }

            return (lutR, lutG, lutB, lutGrey);
        }

        /// <summary>
        /// Checks if a calibration profile is available and valid for the given gamma mode.
        /// </summary>
        public static bool CanUseCalibratedLut(DisplayCalibrationProfile? profile)
        {
            if (profile == null)
                return false;

            // Check if the profile has valid tone curve data
            bool hasToneCurves = profile.RedToneCurve != null &&
                                 profile.GreenToneCurve != null &&
                                 profile.BlueToneCurve != null &&
                                 profile.RedToneCurve.Length > 0;

            return hasToneCurves || profile.MeasuredGamma > 0;
        }

        #endregion
    }
}
