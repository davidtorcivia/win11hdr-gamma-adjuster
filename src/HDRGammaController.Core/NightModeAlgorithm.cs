namespace HDRGammaController.Core
{
    public enum NightModeAlgorithm
    {
        /// <summary>
        /// Standard approximation (Tanner Helland's algorithm).
        /// Provides a pleasant, warm, photo-realistic tint.
        /// </summary>
        Standard,
        
        /// <summary>
        /// Physically accurate conversion based on the CIE 1931 color space.
        /// Simulates a true black-body radiator color.
        /// </summary>
        AccurateCIE1931,
        
        /// <summary>
        /// Specifically targets blue light reduction (460-480nm range) for circadian rhythm.
        /// Less color accurate, but potentially better for sleep.
        /// </summary>
        BlueReduction
    }
}
