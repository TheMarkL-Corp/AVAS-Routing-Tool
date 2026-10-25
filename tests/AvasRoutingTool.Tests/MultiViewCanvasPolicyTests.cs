using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class MultiViewCanvasPolicyTests
    {
        [Fact]
        public void Choose_RealFourKDisplay_UsesItsNativeTiming()
        {
            var canvas = MultiViewCanvasPolicy.Choose(EdidInfo.Parse(TestEdids.ViewSonic4k));

            Assert.True(canvas.FromDisplay);
            Assert.Equal(3840, canvas.Width);
            Assert.Equal(2160, canvas.Height);
            Assert.Equal(60, canvas.Fps);
            Assert.Contains("VP3268-4K", canvas.Description);
        }

        [Fact]
        public void Choose_FullHdDisplay_UsesFullHd()
        {
            var canvas = MultiViewCanvasPolicy.Choose(EdidInfo.Parse(TestEdids.Build((1920, 1080, 60, false))));

            Assert.True(canvas.FromDisplay);
            Assert.Equal((1920, 1080, 60), (canvas.Width, canvas.Height, canvas.Fps));
        }

        [Fact]
        public void Choose_NoEdid_FallsBackToFullHdAndSaysWhy()
        {
            var canvas = MultiViewCanvasPolicy.Choose(null);

            Assert.False(canvas.FromDisplay);
            Assert.Equal((1920, 1080, 60), (canvas.Width, canvas.Height, canvas.Fps));
            Assert.Contains("EDID", canvas.Description);
        }

        [Fact]
        public void Choose_BadChecksum_IsNotTrusted()
        {
            var canvas = MultiViewCanvasPolicy.Choose(EdidInfo.Parse(TestEdids.Build(false, (3840, 2160, 60, false))));

            Assert.False(canvas.FromDisplay);
            Assert.Equal(1920, canvas.Width);
            Assert.Contains("checksum", canvas.Description);
        }

        [Fact]
        public void Choose_InterlacedPreferred_SkipsToTheNextProgressiveTiming()
        {
            var edid = EdidInfo.Parse(TestEdids.Build((1920, 1080, 30, true), (1280, 720, 60, false)));

            var canvas = MultiViewCanvasPolicy.Choose(edid);

            Assert.True(canvas.FromDisplay);
            Assert.Equal((1280, 720, 60), (canvas.Width, canvas.Height, canvas.Fps));
        }

        [Fact]
        public void Choose_PreferredAboveFourK_UsesTheLargestTimingWithinFourK()
        {
            var edid = EdidInfo.Parse(TestEdids.Build((5120, 2880, 60, false), (3840, 2160, 30, false)));

            var canvas = MultiViewCanvasPolicy.Choose(edid);

            Assert.True(canvas.FromDisplay);
            Assert.Equal((3840, 2160, 30), (canvas.Width, canvas.Height, canvas.Fps));
        }

        [Fact]
        public void Choose_OnlyUnusableTimings_FallsBack()
        {
            // 1366 is not a multiple of 8, which layout create requires.
            var edid = EdidInfo.Parse(TestEdids.Build((1366, 768, 60, false)));

            var canvas = MultiViewCanvasPolicy.Choose(edid);

            Assert.False(canvas.FromDisplay);
            Assert.Equal(1920, canvas.Width);
        }

        [Fact]
        public void Choose_RoundsFractionalRefreshRates()
        {
            Assert.Equal(60, MultiViewCanvasPolicy.Choose(EdidInfo.Parse(TestEdids.Build((1920, 1080, 59.94, false)))).Fps);
            Assert.Equal(30, MultiViewCanvasPolicy.Choose(EdidInfo.Parse(TestEdids.Build((1920, 1080, 29.97, false)))).Fps);
        }

        [Fact]
        public void Choose_UsesTheSuppliedFallback()
        {
            var fallback = new MultiViewCanvas(1280, 720, 50, false, "custom");

            var canvas = MultiViewCanvasPolicy.Choose(null, fallback);

            Assert.Equal((1280, 720, 50), (canvas.Width, canvas.Height, canvas.Fps));
        }

        [Fact]
        public void EstimateGbps_FourFullHdWindowsAtSixtyIsAboutTwelve()
        {
            var plan = MultiViewPresets.Build("matrix2x2", 3840, 2160);

            double gbps = MultiViewCanvasPolicy.EstimateGbps(plan, 60);

            Assert.InRange(gbps, 11.9, 12.0); // 3840*2160*60*24 bit
        }

        [Fact]
        public void EstimateGbps_TwoHalfSizeWindows_IsFarBelowTheBudget()
        {
            var plan = MultiViewPresets.Build("side_by_side", 1920, 1080);

            Assert.True(MultiViewCanvasPolicy.EstimateGbps(plan, 60) < MultiViewCanvasPolicy.RecommendedGbps);
        }

        [Theory]
        [InlineData("matrix2x2", true)]   // ~11.9 Gbps at 4K60
        [InlineData("pip", true)]         // ~12.7 Gbps
        [InlineData("quad_1_3", true)]    // ~9.3 Gbps
        [InlineData("side_by_side", false)] // ~6.0 Gbps
        public void NeedsFrameDivider_FourKSixty_OnlyForPlansOverTheBudget(string id, bool expected)
        {
            var plan = MultiViewPresets.Build(id, 3840, 2160);

            Assert.Equal(expected, MultiViewCanvasPolicy.NeedsFrameDivider(plan, 60));
        }

        [Fact]
        public void NeedsFrameDivider_FullHdAtSixty_IsNeverNeeded()
        {
            foreach (var id in MultiViewPresets.Ids)
                Assert.False(MultiViewCanvasPolicy.NeedsFrameDivider(MultiViewPresets.Build(id, 1920, 1080), 60), id);
        }

        [Fact]
        public void NeedsFrameDivider_FourKAtThirty_IsNotNeeded()
        {
            Assert.False(MultiViewCanvasPolicy.NeedsFrameDivider(MultiViewPresets.Build("matrix2x2", 3840, 2160), 30));
        }

        [Fact]
        public void Summarize_UnderBudget_JustStatesTheOutput()
        {
            var canvas = MultiViewCanvasPolicy.Choose(EdidInfo.Parse(TestEdids.ViewSonic4k));
            var light = MultiViewPresets.Build("side_by_side", canvas.Width, canvas.Height);

            string text = MultiViewCanvasPolicy.Summarize(canvas, light);

            Assert.Equal("OUT 3840x2160@60 (VP3268-4K)", text);
        }

        [Fact]
        public void Summarize_OverBudget_ExplainsThatSourcesRunAtHalfFrameRate()
        {
            var canvas = MultiViewCanvasPolicy.Choose(EdidInfo.Parse(TestEdids.ViewSonic4k));
            var heavy = MultiViewPresets.Build("matrix2x2", canvas.Width, canvas.Height);

            string text = MultiViewCanvasPolicy.Summarize(canvas, heavy);

            Assert.Contains("half frame rate", text);
            Assert.Contains("~6.0 Gbps", text);
            Assert.DoesNotContain("recommended", text);
        }

        [Fact]
        public void Summarize_StillOverBudgetAtHalfRate_WarnsPlainly()
        {
            var canvas = new MultiViewCanvas(3840, 2160, 240, true, "3840x2160@240 (test)");
            var heavy = MultiViewPresets.Build("matrix2x2", 3840, 2160);

            string text = MultiViewCanvasPolicy.Summarize(canvas, heavy);

            Assert.Contains("recommended", text);
        }
    }
}
