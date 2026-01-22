using System;
using System.Collections.Concurrent;
using System.Linq;

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
    }
}
