using System.Linq;
using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>Figures come from the live netstat run of 2026-11-07 and the vendor/field-note references in the study.</summary>
    public class MultiViewBandwidthBudgetTests
    {
        private static readonly SourceVideo Uhd60 = new(3840, 2160, 60);
        private static readonly SourceVideo Fhd60 = new(1920, 1080, 60);

        [Fact]
        public void WireGbps_FullHdSixty_MatchesVendorThreePointTwo() =>
            Assert.InRange(MultiViewBandwidthBudget.WireGbps(1920, 1080, 60), 3.15, 3.25);

        [Fact]
        public void Stream0_FourKSixty_IsCompressedToAboutEightAndAHalf()
        {
            var (gbps, compressed) = MultiViewBandwidthBudget.Stream0(Uhd60, halved: false);

            Assert.True(compressed);
            Assert.InRange(gbps, 8.4, 8.7); // live Boom 8.57
        }

        [Fact]
        public void Stream0_FourKSixtyHalved_IsUncompressedAndUsable()
        {
            var (gbps, compressed) = MultiViewBandwidthBudget.Stream0(Uhd60, halved: true);

            Assert.False(compressed);
            Assert.InRange(gbps, 6.3, 6.6); // live VEGA 6.28
        }

        [Fact]
        public void TxEgress_VegaHalvedWithFullHdScaler_MatchesLiveSevenPointNine()
        {
            var load = MultiViewBandwidthBudget.TxEgress(Uhd60, true, (1920, 1080), true);

            Assert.InRange(load.TotalGbps, 7.9, 8.2); // live 7.90
            Assert.False(MultiViewBandwidthBudget.IsOverLimit(load.TotalGbps));
        }

        [Fact]
        public void TxEgress_BoomWithCompressedStreamZeroAndScaler_IsOverTheLimit()
        {
            var load = MultiViewBandwidthBudget.TxEgress(Uhd60, false, (1920, 1080), true);

            Assert.True(load.Stream0Compressed);
            Assert.True(MultiViewBandwidthBudget.IsOverLimit(load.TotalGbps)); // live 9.30
        }

        [Fact]
        public void TxEgress_NoScaler_HasNoStreamOne()
        {
            var load = MultiViewBandwidthBudget.TxEgress(Fhd60, false, null, false);

            Assert.Equal(0, load.Stream1Gbps);
            Assert.Equal(load.Stream0Gbps, load.TotalGbps);
        }

        [Fact]
        public void RxIngress_TwoFullHdThirtyStreams_MatchesLiveThreePointOne()
        {
            double one = MultiViewBandwidthBudget.Stream1(1920, 1080, 60, halved: true);

            Assert.InRange(MultiViewBandwidthBudget.RxIngress(new[] { one, one }), 3.1, 3.3); // live 3.13
        }

        [Fact]
        public void RxIngress_ThreeFullHdSixtyStreams_IsOver()
        {
            double one = MultiViewBandwidthBudget.Stream1(1920, 1080, 60, halved: false);

            Assert.True(MultiViewBandwidthBudget.IsOverLimit(MultiViewBandwidthBudget.RxIngress(new[] { one, one, one })));
        }

        [Fact]
        public void Describe_WithinLimit_IsNull() =>
            Assert.Null(MultiViewBandwidthBudget.Describe("VEGA", "sends", 7.9));

        [Fact]
        public void Describe_OverLimit_NamesDeviceAndFigure()
        {
            string? text = MultiViewBandwidthBudget.Describe("Boom", "sends", 10.2);

            Assert.NotNull(text);
            Assert.Contains("Boom", text);
            Assert.Contains("10.2", text);
        }

        // ---- Divider decision from real sources (spec 11.3, items 2 and 3) ----

        private static SourcePlan Src(SourceVideo video, int w, int h) => new(video, w, h);

        [Fact]
        public void NeedsFrameDivider_Empty_IsFalse() =>
            Assert.False(MultiViewBandwidthBudget.NeedsFrameDivider(System.Array.Empty<SourcePlan>()));

        [Fact]
        public void NeedsFrameDivider_ThirtyHertzSources_AreNotHalvedJustBecauseTheCanvasRunsAtSixty()
        {
            // Four full-HD tiles from 30 Hz sources: 4 x 1.67 = 6.7 Gbps. The canvas is 4K60, whose window pixels alone would say 12.8.
            var sources = Enumerable.Range(0, 4).Select(_ => Src(new SourceVideo(1920, 1080, 30), 1920, 1080)).ToList();

            Assert.False(MultiViewBandwidthBudget.NeedsFrameDivider(sources));
        }

        [Fact]
        public void NeedsFrameDivider_ThreeFullHdSixtyStreams_AreOverTheReceiverLimit()
        {
            var sources = Enumerable.Range(0, 3).Select(_ => Src(Fhd60, 1920, 1080)).ToList();

            Assert.True(MultiViewBandwidthBudget.NeedsFrameDivider(sources)); // 3 x 3.21 = 9.6
        }

        [Fact]
        public void NeedsFrameDivider_UsesTheWireEfficiency()
        {
            // 3840x2160 tile from a 43 Hz source: 8.56 Gbps of pixels, 9.2 on the wire. A small source keeps stream 0 out of it.
            var one = new[] { Src(new SourceVideo(1280, 720, 43), 3840, 2160) };

            Assert.True(MultiViewBandwidthBudget.NeedsFrameDivider(one));
        }

        [Fact]
        public void NeedsFrameDivider_BoomCase_StreamZeroPlusAScaledStreamTipsTheTransmitterOver()
        {
            // One tile only (3.2 Gbps at the receiver), but a 4K60 source's own compressed stream 0 is 8.56 on the same link.
            var boom = new[] { Src(Uhd60, 1920, 1080) };

            Assert.True(MultiViewBandwidthBudget.NeedsFrameDivider(boom));
        }

        [Fact]
        public void NeedsFrameDivider_SmallSourceWithAScaler_Fits()
        {
            var fits = new[] { Src(Fhd60, 1920, 1080), Src(Fhd60, 1920, 1080) };

            Assert.False(MultiViewBandwidthBudget.NeedsFrameDivider(fits)); // 6.4 Gbps at the receiver, 6.4 per transmitter
        }
    }
}
