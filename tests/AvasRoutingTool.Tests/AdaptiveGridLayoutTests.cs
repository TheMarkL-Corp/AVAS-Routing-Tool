using AvasRoutingTool.Controls;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class AdaptiveGridLayoutTests
    {
        private static readonly GridSpec Spec = GridSpec.Default;

        [Fact]
        public void TheCardCaps_LeaveRoomForATallerOrBiggerWindow()
        {
            // Full HD is height-bound, so the caps do not change what 1080p shows; they only stop capping a taller window (UI v2, D9).
            Assert.Equal(960, GridSpec.DisplayCard.MaxCardWidth);
            Assert.Equal(170, GridSpec.DisplayCard.ChromeHeight);
            Assert.Equal(26, GridSpec.DisplayCard.HorizontalPadding);
        }

        [Fact]
        public void ADisplayCard_OnATallWindow_IgnoresHeight_AndUsesTheCarouselWidth()
        {
            var plan = AdaptiveGridLayout.Compute(1, 1888, 1200, GridSpec.DisplayCard);
            Assert.InRange(plan.CardWidth, 525.71 - 0.5, 525.71 + 0.5);
        }

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

        [Fact]
        public void Compute_DisplayCardSpec_Has170PxChrome_ForTheNonOccludingActionBarAndStatusDock()
        {
            // Frame 2 + padding 24 + header 24 + action bar 56 (48 px buttons) + status dock 64: nothing lies on the 16:9 picture.
            Assert.Equal(170, GridSpec.DisplayCard.ChromeHeight);
            Assert.Equal(26, GridSpec.DisplayCard.HorizontalPadding);

            var plan = AdaptiveGridLayout.Compute(2, 1888, 435, GridSpec.DisplayCard);
            double videoHeight = plan.CardHeight - GridSpec.DisplayCard.ChromeHeight;
            double videoWidth = plan.CardWidth - GridSpec.DisplayCard.HorizontalPadding;
            Assert.InRange(videoWidth / videoHeight, 16.0 / 9 * 0.96, 16.0 / 9 * 1.04);
        }

        [Fact]
        public void Carousel_OneCard_Is526Wide()
        {
            var plan = AdaptiveGridLayout.Compute(1, 1888, 435, GridSpec.DisplayCard);

            Assert.InRange(plan.CardWidth, 525.71 - 0.5, 525.71 + 0.5);
            Assert.Equal(1, plan.Rows);
            Assert.Equal(1, plan.Columns);
            Assert.False(plan.NeedsScroll);
        }

        [Fact]
        public void Carousel_FourCards_FitWithoutScroll()
        {
            var plan = AdaptiveGridLayout.Compute(4, 1888, 435, GridSpec.DisplayCard);

            Assert.InRange(plan.CardWidth, 459.5, 460.5);
            Assert.Equal(1, plan.Rows);
            Assert.Equal(4, plan.Columns);
            Assert.False(plan.NeedsScroll);
        }

        [Fact]
        public void Carousel_FiveCards_Scrolls()
        {
            var plan = AdaptiveGridLayout.Compute(5, 1888, 435, GridSpec.DisplayCard);

            Assert.InRange(plan.CardWidth, 525.71 - 0.5, 525.71 + 0.5);
            Assert.Equal(1, plan.Rows);
            Assert.Equal(5, plan.Columns);
            Assert.True(plan.NeedsScroll);
        }

        [Fact]
        public void Carousel_At992_Is400()
        {
            var plan = AdaptiveGridLayout.Compute(5, 992, 435, GridSpec.DisplayCard);

            Assert.Equal(400, plan.CardWidth, 3);
            Assert.True(plan.NeedsScroll);
        }

        [Fact]
        public void Carousel_At1232_UsesCarouselMin400()
        {
            // (1232 - 48) / 3.5 = 338, below the carousel minimum
            var plan = AdaptiveGridLayout.Compute(5, 1232, 435, GridSpec.DisplayCard);

            Assert.Equal(400, plan.CardWidth, 3);
            Assert.Equal(400, GridSpec.DisplayCard.CarouselMinCardWidth);
            Assert.Equal(0, GridSpec.Default.CarouselMinCardWidth);
            Assert.True(plan.NeedsScroll);
        }

        [Fact]
        public void Carousel_At1888_StaysW35_WithTheCarouselMin()
        {
            Assert.InRange(AdaptiveGridLayout.Compute(5, 1888, 435, GridSpec.DisplayCard).CardWidth, 525.21, 526.21);
            Assert.InRange(AdaptiveGridLayout.Compute(2, 1888, 435, GridSpec.DisplayCard).CardWidth, 525.21, 526.21);
        }

        [Fact]
        public void Carousel_Twelve_OneRow()
        {
            var plan = AdaptiveGridLayout.Compute(12, 1888, double.PositiveInfinity, GridSpec.DisplayCard);

            Assert.Equal(1, plan.Rows);
            Assert.Equal(12, plan.Columns);
            Assert.True(plan.NeedsScroll);
            Assert.Equal(AdaptiveGridLayout.CardHeightFor(plan.CardWidth, GridSpec.DisplayCard), plan.CardHeight, 3);
        }

        [Fact]
        public void FitAll_Default_HasNoCarousel()
        {
            Assert.Equal(0, GridSpec.Default.CarouselVisibleCards);
            Assert.Equal(3.5, GridSpec.DisplayCard.CarouselVisibleCards);
        }
    }
}
