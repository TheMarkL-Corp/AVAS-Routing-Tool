using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Controls;
using AvasRoutingTool.Services;
using AvasRoutingTool.Views;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>Composer round 3 gaps: a steady status row, a current step, one-line tile text, one heading weight, a calm frame, one check colour.</summary>
    public partial class ViewSmokeTests
    {
        private static Color ColorOf(string brushKey) => ((SolidColorBrush)Application.Current.FindResource(brushKey)).Color;

        private static MultiViewComposerView StagedComposerView(ComposerRig rig, bool armed)
        {
            new ThemeService().SetTheme(ThemeMode.Dark);
            var c = rig.Composer("quad_1_3");
            c.Arm(rig.Tx1); c.TapBlock(0);
            if (!armed) c.Disarm();
            return rig.View(c);
        }

        [Theory]
        [InlineData(1024, 720)]
        [InlineData(1920, 1080)]
        public void ComposerStatusRow_ArmingDoesNotMoveTheCanvas(double w, double h) =>
            RunSta(() =>
            {
                double idleTop = 0, armedTop = 0;
                var idle = StagedComposerView(new ComposerRig(), armed: false);
                ShowingAt(idle, w, h, () => idleTop = BoundsIn(VisibleNamed(idle, "CanvasFrame"), idle).Top);
                var armed = StagedComposerView(new ComposerRig(), armed: true);
                ShowingAt(armed, w, h, () =>
                {
                    armedTop = BoundsIn(VisibleNamed(armed, "CanvasFrame"), armed).Top;
                    var head = BoundsIn(VisibleNamed(armed, "StepPlaceHeading"), armed);
                    var done = BoundsIn(VisibleNamed(armed, "DoneButton"), armed);
                    if (w >= 1920) Assert.True(Math.Abs((head.Top + head.Bottom) / 2 - (done.Top + done.Bottom) / 2) <= EdgeTolerance, $"step 2 centre {(head.Top + head.Bottom) / 2:F1} vs Done placing centre {(done.Top + done.Bottom) / 2:F1}");
                });
                Assert.True(Math.Abs(idleTop - armedTop) <= EdgeTolerance, $"canvas frame top {idleTop:F1} idle vs {armedTop:F1} armed");
            });

        [Fact]
        public void ComposerSteps_TheFirstUndoneStepIsCurrent() =>
            RunSta(() =>
            {
                new ThemeService().SetTheme(ThemeMode.Dark);
                var rig = new ComposerRig();
                var c = rig.Composer();
                Assert.True(c.StepLayoutCurrent);
                Assert.False(c.StepPlaceCurrent);
                Assert.False(c.StepTakeCurrent);
                var view = rig.View(c);
                ShowingAt(view, 1920, 1080, () =>
                {
                    Assert.True(((StepHeader)VisibleNamed(view, "StepLayoutHeading")).IsCurrent);
                    Assert.False(((StepHeader)VisibleNamed(view, "StepPlaceHeading")).IsCurrent);
                });
                var staged = rig.Composer("quad_1_3");
                Assert.False(staged.StepLayoutCurrent);
                Assert.True(staged.StepPlaceCurrent);
                Assert.False(staged.StepTakeCurrent);
            });

        [Fact]
        public void ComposerLayoutTiles_SubtitlesStayOnOneLineAtTheNarrowRail() =>
            RunSta(() =>
            {
                new ThemeService().SetTheme(ThemeMode.Dark);
                var rig = new ComposerRig();
                var view = rig.View(rig.Composer("quad_1_3"));
                ShowingAt(view, 1024, 720, () =>
                {
                    var tiles = Named(view, "LayoutTile").Where(t => t.IsVisible && t.ActualWidth > 0).ToList();
                    Assert.True(tiles.Count >= 3);
                    var label = (Style)Application.Current.FindResource("Style.Text.ComposerLabel");
                    foreach (var t in tiles)
                    {
                        var sub = FindAll<TextBlock>(t).Single(x => ReferenceEquals(x.Style, label));
                        Assert.True(sub.ActualHeight < sub.FontSize * 2, $"subtitle '{sub.Text}' wraps ({sub.ActualHeight:F1} px high)");
                    }
                });
            });

        [Fact]
        public void ComposerHeadings_ShareOneWeightAndNoStatusOutranksTheStepTitle() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = StagedComposerView(rig, armed: false);
                ShowingAt(view, 1920, 1080, () =>
                {
                    var step = FindAll<TextBlock>(VisibleNamed(view, "StepLayoutHeading")).First(t => t.Name == "LabelText");
                    var status = (TextBlock)VisibleNamed(view, "StagedLabel");
                    Assert.Equal(step.FontWeight, status.FontWeight);
                    Assert.True(status.FontSize <= step.FontSize, $"status {status.FontSize} vs step title {step.FontSize}");
                    var rail = (Style)Application.Current.FindResource("Style.Text.RailHeading");
                    var railWeight = rail.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == TextBlock.FontWeightProperty)?.Value;
                    Assert.True(railWeight == null || (FontWeight)railWeight == step.FontWeight, "the rail heading sets its own weight");
                });
            });

        [Fact]
        public void ComposerCanvasFrame_IdleIsTwoPxStrongBorder() =>
            RunSta(() =>
            {
                new ThemeService().SetTheme(ThemeMode.Dark);
                var rig = new ComposerRig();
                var view = rig.View(rig.Composer());
                ShowingAt(view, 1920, 1080, () =>
                {
                    var frame = (System.Windows.Shapes.Rectangle)Named(view, "CanvasFrame").Single();
                    Assert.Equal(2.0, frame.StrokeThickness);
                    Assert.Equal(ColorOf("Brush.Border.Strong"), ((SolidColorBrush)frame.Stroke).Color);
                });
            });

        [Fact]
        public void ComposerDoneAndCheck_UseNeutralColours() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = StagedComposerView(rig, armed: true);
                ShowingAt(view, 1920, 1080, () =>
                {
                    var done = (Button)VisibleNamed(view, "DoneButton");
                    Assert.Equal(ColorOf("Brush.Border.Control"), ((SolidColorBrush)done.BorderBrush).Color);
                    var check = FindAll<MaterialIcon>(VisibleNamed(view, "StepLayoutHeading")).Single(i => i.Name == "DoneCheck");
                    Assert.Equal(ColorOf("Brush.Text.Primary"), ((SolidColorBrush)check.Foreground).Color);
                });
            });
    }
}
