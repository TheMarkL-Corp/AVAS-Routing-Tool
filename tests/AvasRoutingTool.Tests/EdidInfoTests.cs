using System;
using System.Linq;
using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public static class TestEdids
    {
        /// <summary>Real EDID read from RX1's display (ViewSonic VP3268-4K, 3840x2160@60), 256 bytes.</summary>
        public const string ViewSonic4k =
            "00ffffffffffff005a63348c11010101091c0103804627782e6875a6564fa2260e5054bfef80d100d1c0b300a94095008180810081c04dd000a0f0703e8030203500b9882100001a000000ff005630453138303930303136370a000000fd00184b0f873c000a202020202020000000fc005650333236382d344b0a202020018b020348f15e61605f5e5d6b6665010204030506070809101112131415161d1e1f20212223090707830100006d030c001000383c20006001020367d85dc401788803e3050000e20fe3a36600a0f0701f8030203500b9882100001a1a6800a0f0381f4030203a00b9882100001a4d6c80a070703e8030203a00b9882100001a00ab";

        /// <summary>Real EDID read from the PAX355S decoder's panel (3840x2160@60).</summary>
        public const string Pax355s =
            "00ffffffffffff002e8d25c80000000003200103804728782a7565b04e2ebc260e5054a56b80d1c081c081008180a9c0b300010101014dd000a0f0703e8030203500c48f2100001a000000ff000a202020202020202020202020000000fd002d3d1f8748000a202020202020000000fc00414456414e544543480a202020017d020354f14a01021113041f905f606123090707830100006d030c00100038442000210000006dd85dc40178886010307883641d6d1a000002033078e606752c752ce200d5e305c301e40f000300e6060701757500000000000000000000000000000000000000565e00a0a0a0295030203500c48f210000210000000000000028";

        /// <summary>
        /// Builds a valid 128-byte EDID whose detailed timings are the given ones (first = preferred).
        /// </summary>
        public static string Build(params (int w, int h, double hz, bool interlaced)[] timings) => Build(true, timings);

        public static string Build(bool validChecksum, params (int w, int h, double hz, bool interlaced)[] timings)
        {
            var b = new byte[128];
            new byte[] { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 }.CopyTo(b, 0);
            b[8] = 0x1C; b[9] = 0xF0; // "ACR"-ish, irrelevant
            b[18] = 1; b[19] = 3;

            for (int i = 0; i < Math.Min(4, timings.Length); i++)
            {
                var (w, h, hz, il) = timings[i];
                int hBlank = w >= 3840 ? 560 : w >= 1920 ? 280 : 160;
                int vBlank = h >= 2160 ? 90 : h >= 1080 ? 45 : 30;
                long clock10k = (long)Math.Round(hz * (w + hBlank) * (h + vBlank) / 10_000.0);
                int o = 54 + 18 * i;
                b[o] = (byte)(clock10k & 0xFF);
                b[o + 1] = (byte)((clock10k >> 8) & 0xFF);
                b[o + 2] = (byte)(w & 0xFF);
                b[o + 3] = (byte)(hBlank & 0xFF);
                b[o + 4] = (byte)(((w >> 8) << 4) | (hBlank >> 8));
                b[o + 5] = (byte)(h & 0xFF);
                b[o + 6] = (byte)(vBlank & 0xFF);
                b[o + 7] = (byte)(((h >> 8) << 4) | (vBlank >> 8));
                b[o + 17] = (byte)(il ? 0x80 : 0x00);
            }

            int sum = 0;
            for (int i = 0; i < 127; i++) sum += b[i];
            b[127] = (byte)((256 - sum % 256) % 256);
            if (!validChecksum) b[127] ^= 0x55;
            return Convert.ToHexString(b).ToLowerInvariant();
        }
    }

    public class EdidInfoTests
    {
        [Fact]
        public void Parse_RealViewSonic4kEdid_ReadsPreferredTimingNameAndManufacturer()
        {
            var edid = EdidInfo.Parse(TestEdids.ViewSonic4k);

            Assert.NotNull(edid);
            Assert.Equal("VSC", edid!.ManufacturerId);
            Assert.Equal("VP3268-4K", edid.MonitorName);
            Assert.True(edid.ChecksumValid);
            Assert.NotNull(edid.Preferred);
            Assert.Equal(3840, edid.Preferred!.Width);
            Assert.Equal(2160, edid.Preferred.Height);
            Assert.Equal(60.0, edid.Preferred.RefreshHz, 1);
            Assert.False(edid.Preferred.Interlaced);
        }

        [Fact]
        public void Parse_RealPaxEdid_ReadsPreferredTimingAndName()
        {
            var edid = EdidInfo.Parse(TestEdids.Pax355s);

            Assert.NotNull(edid);
            Assert.Equal("ADVANTECH", edid!.MonitorName);
            Assert.Equal(3840, edid.Preferred!.Width);
            Assert.Equal(2160, edid.Preferred.Height);
            Assert.Equal(60.0, edid.Preferred.RefreshHz, 1);
        }

        [Fact]
        public void Parse_CollectsDetailedTimingsFromTheCtaExtensionToo()
        {
            var edid = EdidInfo.Parse(TestEdids.ViewSonic4k)!;

            // Base block has one DTD; the CTA block carries more.
            Assert.True(edid.Timings.Count > 1, "expected extension timings, got " + edid.Timings.Count);
            Assert.Contains(edid.Timings, t => t.Width == 3840 && t.Height == 2160);
            Assert.Same(edid.Preferred, edid.Timings[0]);
        }

        [Fact]
        public void Parse_SyntheticEdid_ReadsTimingsInOrder()
        {
            var hex = TestEdids.Build((1920, 1080, 60, false), (1280, 720, 60, false));

            var edid = EdidInfo.Parse(hex)!;

            Assert.Equal(2, edid.Timings.Count);
            Assert.Equal(1920, edid.Preferred!.Width);
            Assert.Equal(1080, edid.Preferred.Height);
            Assert.Equal(60.0, edid.Preferred.RefreshHz, 1);
            Assert.Equal(1280, edid.Timings[1].Width);
            Assert.True(edid.ChecksumValid);
        }

        [Fact]
        public void Parse_InterlacedFlag_IsReported()
        {
            var edid = EdidInfo.Parse(TestEdids.Build((1920, 1080, 30, true)))!;

            Assert.True(edid.Preferred!.Interlaced);
        }

        [Fact]
        public void Parse_TamperedChecksum_IsFlagged()
        {
            var edid = EdidInfo.Parse(TestEdids.Build(false, (1920, 1080, 60, false)))!;

            Assert.False(edid.ChecksumValid);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not hex at all")]
        [InlineData("00ffffffffffff00")] // header only, far too short
        public void Parse_GarbageOrShortInput_ReturnsNull(string? hex)
        {
            Assert.Null(EdidInfo.Parse(hex));
        }

        [Fact]
        public void Parse_WrongHeader_ReturnsNull()
        {
            string hex = TestEdids.Build((1920, 1080, 60, false));
            hex = "11" + hex.Substring(2);

            Assert.Null(EdidInfo.Parse(hex));
        }

        [Fact]
        public void Parse_ToleratesWhitespaceAndUpperCase()
        {
            string hex = TestEdids.Build((1920, 1080, 60, false)).ToUpperInvariant();
            hex = string.Join(" ", Enumerable.Range(0, hex.Length / 32).Select(i => hex.Substring(i * 32, 32)));

            Assert.NotNull(EdidInfo.Parse(hex));
        }
    }
}
