using System;
using System.Linq;
using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class EdidModesTests
    {
        private static readonly byte[] Header = { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };

        private static void Seal(byte[] block)
        {
            int sum = 0;
            for (int i = 0; i < 127; i++) sum += block[i];
            block[127] = (byte)((256 - sum % 256) % 256);
        }

        /// <summary>Writes a DTD (all values in the EDID's own units) at offset o.</summary>
        private static void PutDtd(byte[] b, int o, int clock10k, int hActive, int hBlank, int vActive, int vBlank, bool interlaced)
        {
            b[o] = (byte)(clock10k & 0xFF);
            b[o + 1] = (byte)((clock10k >> 8) & 0xFF);
            b[o + 2] = (byte)(hActive & 0xFF);
            b[o + 3] = (byte)(hBlank & 0xFF);
            b[o + 4] = (byte)(((hActive >> 8) << 4) | (hBlank >> 8));
            b[o + 5] = (byte)(vActive & 0xFF);
            b[o + 6] = (byte)(vBlank & 0xFF);
            b[o + 7] = (byte)(((vActive >> 8) << 4) | (vBlank >> 8));
            b[o + 17] = (byte)(interlaced ? 0x80 : 0x00);
        }

        private static byte[] BaseBlock(int extensionCount)
        {
            var b = new byte[128];
            Header.CopyTo(b, 0);
            b[8] = 0x1C; b[9] = 0xF0;
            b[18] = 1; b[19] = 3;
            b[126] = (byte)extensionCount;
            return b;
        }

        /// <summary>A CTA-861 extension block holding the given raw data-block collection bytes.</summary>
        private static byte[] CtaBlock(byte[] dataBlocks, int declaredDtdOffset = -1)
        {
            var b = new byte[128];
            b[0] = 0x02; b[1] = 0x03;
            b[2] = (byte)(declaredDtdOffset >= 0 ? declaredDtdOffset : 4 + dataBlocks.Length);
            dataBlocks.CopyTo(b, 4);
            Seal(b);
            return b;
        }

        private static byte[] VideoDataBlock(params byte[] vics) =>
            new byte[] { (byte)((2 << 5) | vics.Length) }.Concat(vics).ToArray();

        private static string Hex(params byte[][] blocks) =>
            Convert.ToHexString(blocks.SelectMany(x => x).ToArray()).ToLowerInvariant();

        private static string BaseWith1080p60PlusCta(byte[] cta)
        {
            var b = BaseBlock(1);
            PutDtd(b, 54, 14850, 1920, 280, 1080, 45, false);
            Seal(b);
            return Hex(b, cta);
        }

        [Fact]
        public void ViewSonic4k_ListsItsNative4K60_And1080p60()
        {
            var edid = EdidInfo.Parse(TestEdids.ViewSonic4k)!;

            Assert.True(edid.Supports(3840, 2160, 60));
            Assert.True(edid.Supports(1920, 1080, 60));
            Assert.Equal(new EdidMode(3840, 2160, 60, false), edid.Native);
        }

        [Fact]
        public void Pax355s_Lists4K60_And1080p60()
        {
            var edid = EdidInfo.Parse(TestEdids.Pax355s)!;

            Assert.True(edid.Supports(3840, 2160, 60));
            Assert.True(edid.Supports(1920, 1080, 60));
            Assert.Equal(new EdidMode(3840, 2160, 60, false), edid.Native);
        }

        [Fact]
        public void SyntheticFullHdOnlyEdid_DoesNotList4K()
        {
            var edid = EdidInfo.Parse(TestEdids.Build((1920, 1080, 60, false)))!;

            Assert.False(edid.Supports(3840, 2160, 60));
            Assert.True(edid.Supports(1920, 1080, 60));
            Assert.Equal(new EdidMode(1920, 1080, 60, false), edid.Native);
        }

        [Fact]
        public void CtaVic_KnownCodes_DecodeToTheirModes()
        {
            Assert.True(CtaVic.TryGet(16, out var m16));
            Assert.Equal(new EdidMode(1920, 1080, 60, false), m16);
            Assert.True(CtaVic.TryGet(97, out var m97));
            Assert.Equal(new EdidMode(3840, 2160, 60, false), m97);
            Assert.True(CtaVic.TryGet(95, out var m95));
            Assert.Equal(new EdidMode(3840, 2160, 30, false), m95);
        }

        [Theory]
        [InlineData(4, 1280, 720, 60, false)]
        [InlineData(5, 1920, 1080, 60, true)]
        [InlineData(19, 1280, 720, 50, false)]
        [InlineData(20, 1920, 1080, 50, true)]
        [InlineData(31, 1920, 1080, 50, false)]
        [InlineData(32, 1920, 1080, 24, false)]
        [InlineData(33, 1920, 1080, 25, false)]
        [InlineData(34, 1920, 1080, 30, false)]
        [InlineData(93, 3840, 2160, 24, false)]
        [InlineData(94, 3840, 2160, 25, false)]
        [InlineData(96, 3840, 2160, 50, false)]
        public void CtaVic_OtherCodes_DecodeToTheirModes(int vic, int w, int h, int hz, bool interlaced)
        {
            Assert.True(CtaVic.TryGet(vic, out var mode));
            Assert.Equal(new EdidMode(w, h, hz, interlaced), mode);
        }

        [Fact]
        public void CtaVic_UnknownCode_IsIgnored()
        {
            Assert.False(CtaVic.TryGet(250, out _));

            // A video data block carrying an unknown VIC contributes nothing and does not throw.
            var edid = EdidInfo.Parse(BaseWith1080p60PlusCta(CtaBlock(VideoDataBlock(250))))!;
            Assert.Single(edid.SupportedModes);
        }

        [Fact]
        public void SupportedModes_HasNoDuplicates()
        {
            foreach (var hex in new[] { TestEdids.ViewSonic4k, TestEdids.Pax355s })
            {
                var modes = EdidInfo.Parse(hex)!.SupportedModes;
                Assert.Equal(modes.Count, modes.Distinct().Count());
            }

            // The 1080p60 DTD and VIC 16 are the same mode and must be listed once.
            var dup = EdidInfo.Parse(BaseWith1080p60PlusCta(CtaBlock(VideoDataBlock(16, 4))))!;
            Assert.Equal(2, dup.SupportedModes.Count);
        }

        [Fact]
        public void SupportedModes_PutsTheNativeFirst_ThenOtherDtds_ThenVics()
        {
            var edid = EdidInfo.Parse(BaseWith1080p60PlusCta(CtaBlock(VideoDataBlock(4, 97))))!;

            Assert.Equal(new EdidMode(1920, 1080, 60, false), edid.SupportedModes[0]);
            Assert.Equal(new EdidMode(1280, 720, 60, false), edid.SupportedModes[1]);
            Assert.Equal(new EdidMode(3840, 2160, 60, false), edid.SupportedModes[2]);
        }

        [Fact]
        public void VideoDataBlock_NativeFlag_IsMaskedOff()
        {
            // 0x80 | 16 = 144 -> VIC 16 flagged as a native format.
            var edid = EdidInfo.Parse(BaseWith1080p60PlusCta(CtaBlock(VideoDataBlock(144, 4))))!;

            Assert.True(edid.Supports(1920, 1080, 60));
            Assert.True(edid.Supports(1280, 720, 60));
        }

        [Fact]
        public void VideoDataBlock_ModesAreAddedEvenWithoutAnyDtdInTheExtension()
        {
            var edid = EdidInfo.Parse(BaseWith1080p60PlusCta(CtaBlock(VideoDataBlock(96, 95))))!;

            Assert.True(edid.Supports(3840, 2160, 50));
            Assert.True(edid.Supports(3840, 2160, 30));
            Assert.False(edid.Supports(3840, 2160, 60));
        }

        [Fact]
        public void InterlacedDtd_IsListedAsInterlaced_AndNeverSatisfiesSupports()
        {
            // 1080i60: per-field DTD, 74.25 MHz, 2200 x 562 (540 active lines per field).
            var b = BaseBlock(0);
            PutDtd(b, 54, 7425, 1920, 280, 540, 22, true);
            PutDtd(b, 72, 7425, 1280, 370, 720, 30, false);
            Seal(b);

            var edid = EdidInfo.Parse(Hex(b))!;

            Assert.Contains(new EdidMode(1920, 1080, 60, true), edid.SupportedModes);
            Assert.False(edid.Supports(1920, 1080, 60));
            // Native is the first PROGRESSIVE DTD, not the interlaced one listed ahead of it.
            Assert.Equal(new EdidMode(1280, 720, 60, false), edid.Native);
            Assert.Equal(new EdidMode(1280, 720, 60, false), edid.SupportedModes[0]);
        }

        [Fact]
        public void Supports_TreatsFiftyNinePointNineFourAsSixty()
        {
            // 1080p 59.94: 148.352 MHz over 2200 x 1125.
            var b = BaseBlock(0);
            PutDtd(b, 54, 14835, 1920, 280, 1080, 45, false);
            Seal(b);

            var edid = EdidInfo.Parse(Hex(b))!;

            Assert.True(edid.Supports(1920, 1080, 60));
            Assert.False(edid.Supports(1920, 1080, 59));
        }

        [Fact]
        public void TruncatedCtaBlock_DoesNotThrow_AndKeepsWhatItCouldRead()
        {
            // The video data block claims 20 VICs but only 2 bytes remain before the DTD offset: the
            // overrunning block is dropped, the base block's timings survive.
            var cta = new byte[128];
            cta[0] = 0x02; cta[1] = 0x03;
            cta[2] = 6;
            cta[4] = (byte)((2 << 5) | 20);
            cta[5] = 97; cta[6] = 95;
            Seal(cta);

            EdidInfo? edid = null;
            var ex = Record.Exception(() => edid = EdidInfo.Parse(BaseWith1080p60PlusCta(cta)));

            Assert.Null(ex);
            Assert.NotNull(edid);
            Assert.True(edid!.Supports(1920, 1080, 60));
            Assert.False(edid.Supports(3840, 2160, 60));
            Assert.Equal(new EdidMode(1920, 1080, 60, false), edid.Native);
        }

        [Fact]
        public void CtaBlockWithDtdOffsetPastTheEnd_DoesNotThrow()
        {
            var cta = CtaBlock(VideoDataBlock(97), declaredDtdOffset: 200);

            EdidInfo? edid = null;
            var ex = Record.Exception(() => edid = EdidInfo.Parse(BaseWith1080p60PlusCta(cta)));

            Assert.Null(ex);
            Assert.True(edid!.Supports(3840, 2160, 60));
        }

        [Fact]
        public void CtaBlockWithZeroLengthAndTrailingGarbage_DoesNotThrow()
        {
            var blocks = new byte[] { 0x40, 0x00, 0xFF, 0xFF }; // zero-length tag-2 block, then junk
            var ex = Record.Exception(() => EdidInfo.Parse(BaseWith1080p60PlusCta(CtaBlock(blocks))));

            Assert.Null(ex);
        }
    }
}
