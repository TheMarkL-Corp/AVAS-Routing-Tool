using AvasRoutingTool.Rtp;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class Yuv422RasterizerTests
    {
        [Fact]
        public void ConvertYuv422SliceToBgra_DecodesBlackMacroPixelCorrectly()
        {
            // Y0=16 (black), U=128 (neutral), Y1=16 (black), V=128 (neutral)
            byte[] src = new byte[] { 16, 128, 16, 128 };
            byte[] dest = new byte[8];

            Yuv422Rasterizer.ConvertYuv422SliceToBgra(src, 0, 4, dest, 0);

            Assert.Equal(0, dest[0]); // B
            Assert.Equal(0, dest[1]); // G
            Assert.Equal(0, dest[2]); // R
            Assert.Equal(255, dest[3]); // A

            Assert.Equal(0, dest[4]); // B
            Assert.Equal(0, dest[5]); // G
            Assert.Equal(0, dest[6]); // R
            Assert.Equal(255, dest[7]); // A
        }

        [Fact]
        public void ConvertYuv422SliceToBgra_DecodesWhiteMacroPixelCorrectly()
        {
            // Y0=235 (white), U=128, Y1=235, V=128
            byte[] src = new byte[] { 235, 128, 235, 128 };
            byte[] dest = new byte[8];

            Yuv422Rasterizer.ConvertYuv422SliceToBgra(src, 0, 4, dest, 0);

            Assert.Equal(255, dest[0]); // B
            Assert.Equal(255, dest[1]); // G
            Assert.Equal(255, dest[2]); // R
            Assert.Equal(255, dest[3]); // A

            Assert.Equal(255, dest[4]); // B
            Assert.Equal(255, dest[5]); // G
            Assert.Equal(255, dest[6]); // R
            Assert.Equal(255, dest[7]); // A
        }
    }
}
