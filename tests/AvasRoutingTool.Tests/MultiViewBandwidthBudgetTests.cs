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
    }
}
