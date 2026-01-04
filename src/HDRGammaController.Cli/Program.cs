using System;
using System.IO;
using HDRGammaController.Core;

namespace HDRGammaController.Cli
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: HDRGammaController.Cli <gamma_mode> <white_level_nits> [output_csv]");
                Console.WriteLine("Modes: 2.2, 2.4, default");
                Console.WriteLine("Example: HDRGammaController.Cli 2.4 200 lut.csv");
                return;
            }

            string modeStr = args[0].ToLowerInvariant();
            if (!double.TryParse(args[1], out double whiteLevel))
            {
                Console.WriteLine("Invalid white level");
                return;
            }

            GammaMode mode = modeStr switch
            {
                "2.2" => GammaMode.Gamma22,
                "2.4" => GammaMode.Gamma24,
                "default" => GammaMode.WindowsDefault,
                "srgb" => GammaMode.WindowsDefault,
                _ => GammaMode.Gamma24 // Default to 2.4 if unrecognized
            };

            Console.WriteLine($"Generating LUT: Gamma {mode}, SdrWhite {whiteLevel} nits");

            try
            {
                double[] lut = LutGenerator.GenerateLut(mode, whiteLevel);

                if (args.Length >= 3)
                {
                    string outputPath = args[2];
                    using (StreamWriter writer = new StreamWriter(outputPath))
                    {
                        writer.WriteLine("Index,Input_Normalized,Input_Nits,Output_Normalized,Output_Nits");
                        for (int i = 0; i < lut.Length; i++)
                        {
                            double inputNorm = i / 1023.0;
                            double inputNits = TransferFunctions.PqEotf(inputNorm);
                            double outputNorm = lut[i];
                            double outputNits = TransferFunctions.PqEotf(outputNorm);
                            
                            writer.WriteLine($"{i},{inputNorm:F6},{inputNits:F2},{outputNorm:F6},{outputNits:F2}");
                        }
                    }
                    Console.WriteLine($"LUT saved to {outputPath}");
                }
                else
                {
                    // Print a few samples
                    Console.WriteLine("Sample Output (every 100th point):");
                    for (int i = 0; i <= lut.Length; i += 100)
                    {
                         // Handle the last point (1023) if loop overshoot
                         int idx = Math.Min(i, 1023);
                         
                         double inputNorm = idx / 1023.0;
                         double inputNits = TransferFunctions.PqEotf(inputNorm);
                         double outputNorm = lut[idx];
                         double outputNits = TransferFunctions.PqEotf(outputNorm);
                         Console.WriteLine($"[{idx}] In: {inputNits:F2} nits -> Out: {outputNits:F2} nits");

                         if (idx == 1023) break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
