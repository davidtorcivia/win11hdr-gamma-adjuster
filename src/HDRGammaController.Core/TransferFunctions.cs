using System;

namespace HDRGammaController.Core
{
    public static class TransferFunctions
    {
        // ST.2084 (PQ) Constants
        private const double M1 = 2610.0 / 4096.0 / 4.0;
        private const double M2 = 2523.0 / 4096.0 * 128.0;
        private const double C1 = 3424.0 / 4096.0;
        private const double C2 = 2413.0 / 4096.0 * 32.0;
        private const double C3 = 2392.0 / 4096.0 * 32.0;

        /// <summary>
        /// ST.2084 PQ EOTF: Converts normalized PQ signal [0-1] to linear nits [0-10000].
        /// </summary>
        public static double PqEotf(double signal)
        {
            // Clamp input
            signal = Math.Clamp(signal, 0.0, 1.0);

            // N = signal ^ (1/m2)
            double N = Math.Pow(signal, 1.0 / M2);

            // L = (max(0, N - c1) / (c2 - c3 * N)) ^ (1/m1)
            double numerator = Math.Max(0, N - C1);
            double denominator = C2 - C3 * N;

            // Avoid division by zero
            if (denominator == 0) return 10000.0; // Should practically not happen for valid range

            double L = Math.Pow(numerator / denominator, 1.0 / M1);

            return L * 10000.0;
        }

        /// <summary>
        /// ST.2084 PQ Inverse EOTF: Converts linear nits [0-10000] to normalized PQ signal [0-1].
        /// </summary>
        public static double PqInverseEotf(double nits)
        {
            // Clamp input
            nits = Math.Clamp(nits, 0.0, 10000.0);
            
            double L = nits / 10000.0;

            // Y = ( (c1 + c2 * L^m1) / (1 + c3 * L^m1) )^m2
            double Lm1 = Math.Pow(L, M1);
            
            double num = C1 + C2 * Lm1;
            double den = 1.0 + C3 * Lm1;

            double N = Math.Pow(num / den, M2);
            
            return N;
        }

        /// <summary>
        /// Inverse sRGB Piecewise EOTF.
        /// Converts Linear Light (nits) to sRGB Signal [0-1].
        /// </summary>
        /// <param name="linearNits">Absolute brightness in nits.</param>
        /// <param name="whiteLevel">SDR White Level in nits (e.g., 80, 200).</param>
        /// <param name="blackLevel">Black level in nits (usually 0).</param>
        /// <returns>Normalized sRGB signal [0-1].</returns>
        public static double SrgbInverseEotf(double linearNits, double whiteLevel, double blackLevel = 0.0)
        {
            if (whiteLevel <= blackLevel) return 0.0;

            // Normalize linear light to [0, 1]
            double linear = (linearNits - blackLevel) / (whiteLevel - blackLevel);
            linear = Math.Clamp(linear, 0.0, 1.0);

            // sRGB Inverse Companding (Linear -> Signal)
            // If linear <= 0.0031308, S = 12.92 * L
            // Else S = 1.055 * L^(1/2.4) - 0.055
            
            if (linear <= 0.0031308)
            {
                return 12.92 * linear;
            }
            else
            {
                return 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
            }
        }
    }
}
