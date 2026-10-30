using System;
using System.Linq;
using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class MultiViewPresetsTests
    {
        [Theory]
        [InlineData("matrix2x2", 4)]
        [InlineData("side_by_side", 2)]
        [InlineData("pip", 2)]
        [InlineData("quad_1_3", 4)]
        public void Build_ReturnsExpectedWindowCount(string id, int expected)
        {
            var plan = MultiViewPresets.Build(id, 1920, 1080);
            Assert.Equal(expected, plan.Slots.Count);
        }

        [Theory]
        [InlineData("single")]
        [InlineData("off")]
        [InlineData("nope")]
        public void Build_NonMultiViewId_Throws(string id)
        {
            Assert.Throws<ArgumentException>(() => MultiViewPresets.Build(id, 1920, 1080));
        }

        [Theory]
        [InlineData("matrix2x2", true)]
        [InlineData("pip", true)]
        [InlineData("side_by_side", true)]
        [InlineData("quad_1_3", true)]
        [InlineData("single", false)]
        [InlineData("off", false)]
        public void IsMultiView_RecognisesPresetIds(string id, bool expected)
        {
            Assert.Equal(expected, MultiViewPresets.IsMultiView(id));
        }

        [Fact]
        public void Matrix2x2_At1080p_TilesFourEqualQuadrants()
        {
            var plan = MultiViewPresets.Build("matrix2x2", 1920, 1080);

            Assert.Equal((0, 0, 960, 540), Rect(plan.Slots[0]));
            Assert.Equal((960, 0, 960, 540), Rect(plan.Slots[1]));
            Assert.Equal((0, 540, 960, 540), Rect(plan.Slots[2]));
            Assert.Equal((960, 540, 960, 540), Rect(plan.Slots[3]));
        }

        [Fact]
        public void SideBySide_At1080p_UsesTwoSixteenByNineWindowsCenteredVertically()
        {
            var plan = MultiViewPresets.Build("side_by_side", 1920, 1080);

            Assert.Equal((0, 270, 960, 540), Rect(plan.Slots[0]));
            Assert.Equal((960, 270, 960, 540), Rect(plan.Slots[1]));
        }

        [Fact]
        public void Pip_At1080p_InsetIsIndexZeroInFrontOfBackgroundIndexOne()
        {
            var plan = MultiViewPresets.Build("pip", 1920, 1080);

            var inset = plan.Slots.Single(s => s.Index == 0);
            var background = plan.Slots.Single(s => s.Index == 1);

            Assert.Equal((0, 0, 1920, 1080), Rect(background));
            Assert.True(inset.Width < background.Width);
            Assert.True(inset.X + inset.Width <= 1920 && inset.Y + inset.Height <= 1080);

            // Paint order is back-to-front: highest API index first, so the inset is painted last.
            Assert.Equal(1, plan.PaintOrder.First().Index);
            Assert.Equal(0, plan.PaintOrder.Last().Index);
        }

        [Fact]
        public void Pip_At1080p_InsetStaysAQuarterOfTheCanvasWidth()
        {
            var plan = MultiViewPresets.Build("pip", 1920, 1080);

            Assert.Equal((1400, 770, 480, 270), Rect(plan.Slots.Single(s => s.Index == 0)));
        }

        [Fact]
        public void Pip_WithInsetWidth_At1080p_InsetIs640x360AtTheBottomRightMargin()
        {
            var plan = MultiViewPresets.Build("pip", 1920, 1080, insetWidth: 640);

            Assert.Equal((1920 - 640 - 40, 1080 - 360 - 40, 640, 360), Rect(plan.Slots.Single(s => s.Index == 0)));
            Assert.Equal((0, 0, 1920, 1080), Rect(plan.Slots.Single(s => s.Index == 1)));
            Assert.Equal(1, plan.PaintOrder.First().Index);
        }

        [Fact]
        public void Pip_WithInsetWidth_OverridesTheFourKFixedInset()
        {
            var plan = MultiViewPresets.Build("pip", 3840, 2160, insetWidth: 960);

            var inset = plan.Slots.Single(s => s.Index == 0);
            Assert.Equal((960, 540), (inset.Width, inset.Height));
        }

        [Fact]
        public void PipLarge_At1080p_InsetIsAThirdOfTheWidthInFrontOfTheMain()
        {
            Assert.True(MultiViewPresets.IsMultiView("pip_large"));

            var plan = MultiViewPresets.Build("pip_large", 1920, 1080);

            var inset = plan.Slots.Single(s => s.Index == 0);
            Assert.Equal("Inset", inset.Label);
            Assert.Equal((640, 360), (inset.Width, inset.Height));
            Assert.Equal((0, 0, 1920, 1080), Rect(plan.Slots.Single(s => s.Index == 1)));
            Assert.Equal(0, plan.PaintOrder.Last().Index);
            Assert.Equal("avas_pip_large_1920x1080", plan.Name);
        }

        [Fact]
        public void PipLarge_NameRoundTripsAndPipStaysPip()
        {
            Assert.True(MultiViewPresets.TryParseName(MultiViewPresets.LayoutName("pip_large", 1920, 1080, "74:FE:48:3C:05:9E"), out var large, out int w, out int h));
            Assert.Equal(("pip_large", 1920, 1080), (large, w, h));

            Assert.True(MultiViewPresets.TryParseName("avas_pip_1920x1080", out var small, out _, out _));
            Assert.Equal("pip", small);
        }

        [Fact]
        public void Pip_WithInsetWidthLargerThanTheCanvas_IsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MultiViewPresets.Build("pip", 1920, 1080, insetWidth: 2000));
        }

        [Fact]
        public void Pip_AtFourK_InsetIsAFixed1280x720AtTheBottomRightMargin()
        {
            var plan = MultiViewPresets.Build("pip", 3840, 2160);

            var inset = plan.Slots.Single(s => s.Index == 0);
            var main = plan.Slots.Single(s => s.Index == 1);

            Assert.Equal("Inset", inset.Label);
            Assert.Equal((0, 0, 3840, 2160), Rect(main));
            Assert.Equal((1280, 720), (inset.Width, inset.Height));
            Assert.Equal((3840 - 1280 - 80, 2160 - 720 - 80), (inset.X, inset.Y));
            Assert.Equal(1, plan.PaintOrder.First().Index);
            Assert.Equal(0, plan.PaintOrder.Last().Index);
        }

        [Fact]
        public void Pip_AtFourK_InsetGetsAStandardSurfaceAndMainTheLargeSurfaceZero()
        {
            var plan = MultiViewPresets.Build("pip", 3840, 2160);

            Assert.Equal(0, plan.Slots.Single(s => s.Index == 1).TargetSurface);
            Assert.InRange(plan.Slots.Single(s => s.Index == 0).TargetSurface, 1, 11);
        }

        [Fact]
        public void Quad1Plus3_At1080p_FocusWindowLeftAndThreeStackedOnTheRight()
        {
            var plan = MultiViewPresets.Build("quad_1_3", 1920, 1080);

            Assert.Equal((0, 180, 1280, 720), Rect(plan.Slots[0]));
            Assert.Equal((1280, 0, 640, 360), Rect(plan.Slots[1]));
            Assert.Equal((1280, 360, 640, 360), Rect(plan.Slots[2]));
            Assert.Equal((1280, 720, 640, 360), Rect(plan.Slots[3]));
        }

        [Theory]
        [InlineData("matrix2x2")]
        [InlineData("pip")]
        [InlineData("side_by_side")]
        [InlineData("quad_1_3")]
        public void EveryPreset_ProducesApiLegalSixteenByNineWindowsInsideTheCanvas(string id)
        {
            foreach (var (w, h) in new[] { (1920, 1080), (3840, 2160), (1920, 1200), (1280, 720) })
            {
                var plan = MultiViewPresets.Build(id, w, h);
                foreach (var s in plan.Slots)
                {
                    Assert.True(s.X % 2 == 0 && s.Y % 2 == 0, $"{id} {w}x{h} slot {s.Index} position must be even");
                    Assert.True(s.Width % 2 == 0 && s.Height % 2 == 0, $"{id} {w}x{h} slot {s.Index} size must be even");
                    Assert.True(s.Width >= 8 && s.Height >= 8);
                    Assert.True(s.X >= 0 && s.Y >= 0 && s.X + s.Width <= w && s.Y + s.Height <= h, $"{id} {w}x{h} slot {s.Index} outside canvas");
                    Assert.InRange(Math.Abs((double)s.Width / s.Height - 16.0 / 9.0), 0, 0.02);
                    Assert.DoesNotContain(s.Width, new[] { 1366, 1368, 2732, 2734, 4098, 4100 });
                }
            }
        }

        [Fact]
        public void EveryPreset_AssignsDistinctApiIndexesAndDistinctSurfaces()
        {
            foreach (var id in new[] { "matrix2x2", "pip", "side_by_side", "quad_1_3" })
            {
                foreach (var (w, h) in new[] { (1920, 1080), (3840, 2160) })
                {
                    var plan = MultiViewPresets.Build(id, w, h);
                    Assert.Equal(plan.Slots.Count, plan.Slots.Select(s => s.Index).Distinct().Count());
                    Assert.Equal(plan.Slots.Count, plan.Slots.Select(s => s.TargetSurface).Distinct().Count());
                    Assert.All(plan.Slots, s => Assert.InRange(s.TargetSurface, 0, 11));
                }
            }
        }

        [Fact]
        public void SurfaceAllocation_OversizedWindowTakesSurfaceZero_OthersTakeOneToEleven()
        {
            var pip4k = MultiViewPresets.Build("pip", 3840, 2160);
            var background = pip4k.Slots.Single(s => s.Width == 3840);
            Assert.Equal(0, background.TargetSurface);
            Assert.All(pip4k.Slots.Where(s => s != background), s => Assert.InRange(s.TargetSurface, 1, 11));

            var matrix1080 = MultiViewPresets.Build("matrix2x2", 1920, 1080);
            Assert.All(matrix1080.Slots, s => Assert.InRange(s.TargetSurface, 1, 11));
        }

        [Fact]
        public void Name_EncodesPresetAndCanvasSoItNeverClashesWithBuiltInLayouts()
        {
            Assert.Equal("avas_matrix2x2_1920x1080", MultiViewPresets.Build("matrix2x2", 1920, 1080).Name);
            Assert.Equal("avas_quad_1_3_3840x2160", MultiViewPresets.Build("quad_1_3", 3840, 2160).Name);
        }

        [Fact]
        public void NormalizedRects_AreFractionsOfTheCanvas()
        {
            var plan = MultiViewPresets.Build("matrix2x2", 1920, 1080);
            var s = plan.Slots[3];
            Assert.Equal(0.5, s.NormalizedX, 3);
            Assert.Equal(0.5, s.NormalizedY, 3);
            Assert.Equal(0.5, s.NormalizedWidth, 3);
            Assert.Equal(0.5, s.NormalizedHeight, 3);
        }

        [Fact]
        public void Build_CanvasTooSmall_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MultiViewPresets.Build("matrix2x2", 16, 9));
        }

        private static (int X, int Y, int W, int H) Rect(WindowSlot s) => (s.X, s.Y, s.Width, s.Height);
    }
}
