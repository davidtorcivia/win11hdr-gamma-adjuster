using Xunit;
using HDRGammaController.Core;
using System.Linq;

namespace HDRGammaController.Tests
{
    public class LutGeneratorTests
    {
        [Fact]
        public void WindowsDefault_IsIdentity()
        {
            double[] lut = LutGenerator.GenerateLut(GammaMode.WindowsDefault, 200.0);

            Assert.Equal(1024, lut.Length);
            Assert.Equal(0.0, lut[0], 6);
            Assert.Equal(1.0, lut[1023], 6);
            
            for(int i=0; i<1024; i++)
            {
                Assert.Equal(i / 1023.0, lut[i], 6);
            }
        }

        [Fact]
        public void Gamma24_Monotonic()
        {
            double[] lut = LutGenerator.GenerateLut(GammaMode.Gamma24, 200.0);

            for(int i=1; i<1024; i++)
            {
                Assert.True(lut[i] >= lut[i-1], $"LUT not monotonic at index {i}");
            }
        }
        
        [Fact]
        public void Gamma24_ShoulderBlend()
        {
            // At max signal (1.0), output should be 1.0 (bypass)
            double[] lut = LutGenerator.GenerateLut(GammaMode.Gamma24, 200.0);
            Assert.Equal(1.0, lut[1023], 6);
        }
    }
}
