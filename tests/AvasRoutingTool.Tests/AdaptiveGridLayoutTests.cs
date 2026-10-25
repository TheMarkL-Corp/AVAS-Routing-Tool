using AvasRoutingTool.Controls;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class AdaptiveGridLayoutTests
    {
        private static readonly GridSpec Spec = GridSpec.Default;

        [Fact]
        public void Compute_NoCards_ReturnsEmptyPlan()
        {
            var plan = AdaptiveGridLayout.Compute(0, 1888, 435, Spec);

            Assert.Equal(0, plan.Columns);
            Assert.Equal(0, plan.Rows);
            Assert.False(plan.NeedsScroll);
        }

        [Fact]
        public void Compute_TwoCardsOnFullHd_GrowToFillRowHeight_NotLeftClustered()
        {
            // 1888 x 435 is the real per-section viewport on a 1920x1080 window.
            var plan = AdaptiveGridLayout.Compute(2, 1888, 435, Spec);

            Assert.Equal(2, plan.Columns);
            Assert.Equal(1, plan.Rows);
            Assert.False(plan.NeedsScroll);
            // Old fixed cards were 440 wide; the new ones must be clearly larger.
            Assert.InRange(plan.CardWidth, 540, 600);
            // And must fit the section height exactly (no clipping, no dead trench).
            Assert.True(plan.CardHeight <= 435 + 0.5);
        }

        [Fact]
        public void Compute_FourCardsOnFullHd_FitInOneRow()
        {
            var plan = AdaptiveGridLayout.Compute(4, 1888, 435, Spec);

            Assert.Equal(4, plan.Columns);
            Assert.Equal(1, plan.Rows);
            Assert.False(plan.NeedsScroll);
            Assert.InRange(plan.CardWidth, 420, 480);
        }

        [Fact]
        public void Compute_SingleCardOnTallScreen_IsCappedAtMaxWidth()
        {
            var plan = AdaptiveGridLayout.Compute(1, 1888, 1200, Spec);

            Assert.Equal(1, plan.Columns);
            Assert.Equal(Spec.MaxCardWidth, plan.CardWidth, 3);
        }

        [Fact]
        public void Compute_TooManyCardsForHeight_FallsBackToMinWidthAndScrolls()
        {
            var plan = AdaptiveGridLayout.Compute(8, 1888, 435, Spec);

            Assert.Equal(Spec.MinCardWidth, plan.CardWidth, 3);
            Assert.True(plan.NeedsScroll);
            Assert.Equal(5, plan.Columns);
            Assert.Equal(2, plan.Rows);
        }

        [Fact]
        public void Compute_UnconstrainedHeight_UsesWidthOnly_AndPrefersFewerRows()
        {
            var plan = AdaptiveGridLayout.Compute(3, 1888, double.PositiveInfinity, Spec);

            Assert.Equal(3, plan.Columns);
            Assert.Equal(1, plan.Rows);
            Assert.Equal(Spec.MaxCardWidth, plan.CardWidth, 3);
            Assert.False(plan.NeedsScroll);
        }

        [Fact]
        public void Compute_ViewportNarrowerThanMinWidth_NeverOverflowsHorizontally()
        {
            var plan = AdaptiveGridLayout.Compute(2, 250, 600, Spec);

            Assert.Equal(1, plan.Columns);
            Assert.True(plan.CardWidth <= 250);
        }

        [Fact]
        public void Compute_CardHeight_KeepsSixteenByNineVideoSurface()
        {
            var plan = AdaptiveGridLayout.Compute(2, 1888, 435, Spec);

            double videoWidth = plan.CardWidth - Spec.HorizontalPadding;
            double expected = videoWidth * 9.0 / 16.0 + Spec.ChromeHeight;
            Assert.Equal(expected, plan.CardHeight, 3);
        }

        [Fact]
        public void Compute_4kViewport_StillCapsCardsSoTheyStayReadable()
        {
            var plan = AdaptiveGridLayout.Compute(2, 3808, 1000, Spec);

            Assert.True(plan.CardWidth <= Spec.MaxCardWidth + 0.001);
            Assert.Equal(2, plan.Columns);
        }
    }
}
