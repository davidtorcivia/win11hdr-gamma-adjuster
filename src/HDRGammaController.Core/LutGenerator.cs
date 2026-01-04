using System;

namespace HDRGammaController.Core
{
    public static class LutGenerator
    {
        /// <summary>
        /// Generates a 1024-point 1D LUT for HDR gamma correction.
        /// </summary>
        /// <param name="gammaMode">The target gamma curve (2.2 or 2.4).</param>
        /// <param name="sdrWhiteLevel">The SDR white level in nits (e.g. 80, 200, 480).</param>
        /// <returns>Array of 1024 doubles representing the output PQ signal [0-1] for each input index.</returns>
        public static double[] GenerateLut(GammaMode gammaMode, double sdrWhiteLevel)
        {
            double[] lut = new double[1024];

            if (gammaMode == GammaMode.WindowsDefault)
            {
                // Identity LUT
                for (int i = 0; i < 1024; i++)
                {
                    lut[i] = i / 1023.0;
                }
                return lut;
            }

            double gamma = gammaMode == GammaMode.Gamma24 ? 2.4 : 2.2;
            double blackLevel = 0.0; // Assuming perfect black for calculation

            for (int i = 0; i < 1024; i++)
            {
                double normalized = i / 1023.0; // Input PQ signal [0-1]

                // 1. PQ EOTF -> linear nits (Input Light)
                double linear = TransferFunctions.PqEotf(normalized);

                // 2. Input Light -> Simulated Signal (using inverse sRGB Piecewise)
                // This asks: "If this light were produced by an sRGB display, what was the signal?"
                double srgbNormalized = TransferFunctions.SrgbInverseEotf(linear, sdrWhiteLevel, blackLevel);

                // 3. Simulated Signal -> Desired Output Light (Power Law)
                // Apply the desired pure power gamma to that signal
                double gammaApplied = Math.Pow(srgbNormalized, gamma);

                // 4. Desired Output Light -> Absolute Nits
                // Scale back to nits
                double outputLinear = blackLevel + (sdrWhiteLevel - blackLevel) * gammaApplied;

                // 5. Absolute Nits -> Output Signal (PQ)
                // Encode for the HDR display
                double output = TransferFunctions.PqInverseEotf(outputLinear);

                // 6. HDR Headroom Preservation
                // Apply full gamma correction in SDR range (0 to SDR white)
                // Only blend toward bypass ABOVE SDR white to preserve HDR highlights
                if (linear <= sdrWhiteLevel)
                {
                    // SDR range: apply full gamma correction
                    lut[i] = output;
                }
                else
                {
                    // HDR headroom region: gradually blend toward passthrough
                    // This ensures HDR highlights are not clipped or distorted
                    double headroomRange = 10000.0 - sdrWhiteLevel;
                    double headroomPosition = (linear - sdrWhiteLevel) / headroomRange;
                    double blendFactor = Math.Min(1.0, headroomPosition);
                    
                    lut[i] = output + (normalized - output) * blendFactor;
                }
            }

            return lut;
        }
    }
}
