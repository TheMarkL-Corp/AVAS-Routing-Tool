using AvasRoutingTool.Rtp;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class Yuv422RasterizerTests
    {
        // RFC 4175 YCbCr-4:2:2 8-bit pixel group = Cb Y0 Cr Y1, limited range, BT.709.
        private static (byte b, byte g, byte r) Decode(byte cb, byte y0, byte cr)
        {
            var bgr = Yuv422Rasterizer.ConvertYuv422ToBgr24(new byte[] { cb, y0, cr, y0 }, 2, 1);
            return (bgr[0], bgr[1], bgr[2]);
        }

        [Fact]
        public void Black_IsZero() => Assert.Equal(((byte)0, (byte)0, (byte)0), Decode(128, 16, 128));

        [Fact]
        public void White_Is255() => Assert.Equal(((byte)255, (byte)255, (byte)255), Decode(128, 235, 128));

        [Fact]
        public void Red_DrivenByCr_NotCb()
        {
            // BT.709 limited red (255,0,0): Y=63 Cb=102 Cr=240. High Cr must make red, not blue.
            var (b, g, r) = Decode(102, 63, 240);
            Assert.True(r > 240, $"r={r}");
            Assert.True(g < 15 && b < 15, $"g={g} b={b}");
        }

        [Fact]
        public void Blue_DrivenByCb_NotCr()
        {
            // BT.709 limited blue (0,0,255): Y=32 Cb=240 Cr=118.
            var (b, g, r) = Decode(240, 32, 118);
            Assert.True(b > 240, $"b={b}");
            Assert.True(r < 15 && g < 15, $"r={r} g={g}");
        }

        [Fact]
        public void RgbAndBgrConvertersAgree()
        {
            byte[] yuv = { 90, 120, 200, 180, 60, 30, 230, 220 };
            var rgb = Yuv422Rasterizer.ConvertYuv422ToRgb24(yuv, 4, 1);
            var bgr = Yuv422Rasterizer.ConvertYuv422ToBgr24(yuv, 4, 1);
            for (int p = 0; p < 4; p++)
            {
                Assert.Equal(rgb[p * 3], bgr[p * 3 + 2]);
                Assert.Equal(rgb[p * 3 + 1], bgr[p * 3 + 1]);
                Assert.Equal(rgb[p * 3 + 2], bgr[p * 3]);
            }
        }

    }
}
