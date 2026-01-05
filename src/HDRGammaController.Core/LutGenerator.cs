using System;

namespace HDRGammaController.Core
{
    public static class LutGenerator
    {
        /// <summary>
        /// Generates a 1024-point 1D LUT for HDR gamma correction (single channel, no calibration).
        /// </summary>
        public static double[] GenerateLut(GammaMode gammaMode, double sdrWhiteLevel, bool isHdr = true)
        {
            return GenerateLut(gammaMode, sdrWhiteLevel, CalibrationSettings.Default, isHdr).Grey;
        }
        
        /// <summary>
        /// Generates per-channel 1024-point 1D LUTs for HDR gamma correction with calibration.
        /// </summary>
        /// <param name="gammaMode">The target gamma curve (2.2 or 2.4).</param>
        /// <param name="sdrWhiteLevel">The SDR white level in nits (e.g. 80, 200, 480).</param>
        /// <param name="calibration">Calibration settings for dimming, temp, tint, RGB.</param>
        /// <returns>Per-channel LUTs (R, G, B) and a Grey reference.</returns>
        public static (double[] R, double[] G, double[] B, double[] Grey) GenerateLut(
            GammaMode gammaMode, 
            double sdrWhiteLevel, 
            CalibrationSettings calibration,
            bool isHdr = true)
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
    }
}
