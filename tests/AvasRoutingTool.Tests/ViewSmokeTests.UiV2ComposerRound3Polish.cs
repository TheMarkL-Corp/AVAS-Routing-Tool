using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Controls;
using AvasRoutingTool.Services;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>Composer round 3 polish: one step-marker baseline, a canvas attached to its status row, a one-line Undo, even layout tiles, a check that matches the marker.</summary>
    public partial class ViewSmokeTests
    {
        [Theory]
        [InlineData(1024, 720)]
        [InlineData(1920, 1080)]
        public void ComposerSteps_OneAndTwoShareABaseline(double w, double h) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, ThemeMode.Dark);
                ShowingAt(view, w, h, () =>
                {
                    var one = BoundsIn(VisibleNamed(view, "StepLayoutHeading"), view);
                    var two = BoundsIn(VisibleNamed(view, "StepPlaceHeading"), view);
                    Assert.True(Math.Abs(one.Top - two.Top) <= EdgeTolerance, $"step 1 top {one.Top:F1} vs step 2 top {two.Top:F1}");
                    Assert.True(Math.Abs(one.Height - two.Height) <= EdgeTolerance, $"step 1 height {one.Height:F1} vs step 2 height {two.Height:F1}");
                });
            });

        [Theory]
        [InlineData(1024, 720)]
        [InlineData(1920, 1080)]
        public void ComposerCanvas_SitsRightUnderItsStatusRow(double w, double h) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, ThemeMode.Dark);
                ShowingAt(view, w, h, () =>
                {
                    var rowBottom = new[] { "StepPlaceHeading", "DoneButton" }.Select(n => BoundsIn(VisibleNamed(view, n), view).Bottom).Max();
                    var frameTop = BoundsIn(VisibleNamed(view, "CanvasFrame"), view).Top;
                    Assert.True(frameTop - rowBottom <= 24, $"canvas frame starts {frameTop - rowBottom:F1} px under its status row");
                });
            });

        [Theory]
        [InlineData(1024, 720)]
        [InlineData(1920, 1080)]
        public void ComposerUndo_IsOneLineAtTheActionBarHeight(double w, double h) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, ThemeMode.Dark);
                ShowingAt(view, w, h, () =>
                {
                    var undo = VisibleNamed(view, "UndoButton");
                    var discard = VisibleNamed(view, "DiscardButton");
                    Assert.True(Math.Abs(undo.ActualHeight - discard.ActualHeight) <= EdgeTolerance, $"Undo {undo.ActualHeight:F1} px high vs Discard {discard.ActualHeight:F1}");
                    var label = FindAll<TextBlock>(undo).Single(t => t.IsVisible);
                    Assert.True(label.ActualHeight < label.FontSize * 2, $"the Undo label wraps ({label.ActualHeight:F1} px high)");
                });
            });

        [Fact]
        public void ComposerLayoutTiles_AreOneHeightAtTheNarrowRail() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, ThemeMode.Dark);
                ShowingAt(view, 1024, 720, () =>
                {
                    var tiles = Named(view, "LayoutTile").Where(t => t.IsVisible && t.ActualWidth > 0).ToList();
                    Assert.True(tiles.Count >= 3);
                    var heights = tiles.Select(t => t.ActualHeight).ToList();
                    Assert.True(heights.Max() - heights.Min() <= EdgeTolerance, "tile heights differ: " + string.Join(", ", heights.Select(x => x.ToString("F1"))));
                });
            });

        [Fact]
        public void ComposerStepCheck_IsTheLayoutIconSize() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, ThemeMode.Dark);
                ShowingAt(view, 1920, 1080, () =>
                {
                    var header = VisibleNamed(view, "StepLayoutHeading");
                    var check = FindAll<MaterialIcon>(header).Single(i => i.Name == "DoneCheck");
                    Assert.Equal((double)Application.Current.FindResource("Size.Icon.Layout"), check.Width);
                });
            });
    }
}
