using Xunit;
using HDRGammaController.Core;
using System;

namespace HDRGammaController.Tests
{
    public class TransferFunctionTests
    {
        [Fact]
        public void PqEotf_Bounds()
        {
            Assert.Equal(0.0, TransferFunctions.PqEotf(0.0), 4);
            Assert.Equal(10000.0, TransferFunctions.PqEotf(1.0), 4);
        }

        [Fact]
        public void PqInverseEotf_Bounds()
        {
            Assert.Equal(0.0, TransferFunctions.PqInverseEotf(0.0), 4);
            Assert.Equal(1.0, TransferFunctions.PqInverseEotf(10000.0), 4);
        }

        [Theory]
        [InlineData(0.1)]
        [InlineData(0.5)]
        [InlineData(0.8)]
        public void Pq_RoundTrip(double signal)
        {
            double nits = TransferFunctions.PqEotf(signal);
            double result = TransferFunctions.PqInverseEotf(nits);
            Assert.Equal(signal, result, 6);
        }

        [Fact]
        public void SrgbInverseEotf_Bounds()
        {
            // Black level
            Assert.Equal(0.0, TransferFunctions.SrgbInverseEotf(0.0, 200.0, 0.0), 4);
            
            // White level (should map to 1.0 signal)
            Assert.Equal(1.0, TransferFunctions.SrgbInverseEotf(200.0, 200.0, 0.0), 4);
        }
    }
}
