using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Services;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Composer round, Task 4: one right edge and one left edge. Back to displays, the right-rail cards, the banner, Save and Apply,
    /// Undo and the Discard / Clear all pair end on the same x (no scrollbar gutter on only some of them), and the buttons of a row sit on
    /// one centre line.
    /// </summary>
    public partial class ViewSmokeTests
    {
        private const double EdgeTolerance = 0.5;

        /// <summary>A Composer in the state that makes every right-rail element real: a second Take (Undo), staged edits (Discard, Clear all, change lines) and an armed source (Done placing, a banner).</summary>
        private static MultiViewComposerView AlignedComposerView(ComposerRig rig, ThemeMode mode, int extraChanges = 0)
        {
            new ThemeService().SetTheme(mode);
            var c = rig.Composer("quad_1_3");
            c.Arm(rig.Tx1); c.TapBlock(0); c.Disarm();
            Pump(c.TakeAsync);
            c.Arm(rig.Tx2); c.TapBlock(1); c.Disarm();
            Pump(c.TakeAsync); // a second Take: Undo is offered
            Assert.True(c.CanUndo);
            for (int i = 0; i < extraChanges; i++)
            {
                c.Arm(i % 2 == 0 ? rig.Vega : rig.Tx1); c.TapBlock(i % 4); c.Disarm();
            }
            c.Arm(rig.Tx1); c.TapBlock(2); c.Disarm();
            c.Arm(rig.Vega); // armed: Done placing and the placing banner
            return rig.View(c);
        }

        private static FrameworkElement VisibleNamed(DependencyObject root, string name)
        {
            var el = Named(root, name).SingleOrDefault(e => e.IsVisible);
            Assert.True(el != null, $"{name} is not visible");
            Assert.True(el!.ActualWidth > 0 && el.ActualHeight > 0, $"{name} has no size");
            return el;
        }

        private static string EdgeReport(FrameworkElement view, IEnumerable<FrameworkElement> els) =>
            string.Join("; ", els.Select(e => $"{e.Name} L={BoundsIn(e, view).Left:F1} R={BoundsIn(e, view).Right:F1}"));

        private static readonly string[] RailNames = { "OnScreenCard", "ChangesCard", "BannerHost", "TakeButton", "UndoButton", "DiscardButton", "ClearAllButton", "CloseComposerButton" };

        private static void AssertRightRailEdges(MultiViewComposerView view)
        {
            var scroll = (ScrollViewer)Named(view, "RightRailScroll").Single();
            var els = RailNames.Select(n => VisibleNamed(view, n)).ToList();
            var report = EdgeReport(view, els) + $"; ScrollableHeight={scroll.ScrollableHeight:F1}";
            var rightEdge = BoundsIn(els[0], view).Right;
            // the Discard / Clear all pair ends at Clear all; Discard is the pair's left half
            foreach (var e in els.Where(x => x.Name != "DiscardButton"))
                Assert.True(Math.Abs(BoundsIn(e, view).Right - rightEdge) <= EdgeTolerance, $"right edges differ: {report}");
            // left edges of the full-width rail elements (Back is right-aligned and shorter; Clear all starts mid-rail)
            var leftEdge = BoundsIn(els[0], view).Left;
            foreach (var e in els.Where(x => x.Name != "CloseComposerButton" && x.Name != "ClearAllButton"))
                Assert.True(Math.Abs(BoundsIn(e, view).Left - leftEdge) <= EdgeTolerance, $"left edges differ: {report}");
        }

        [Theory]
        [InlineData(1024, 720, ThemeMode.Dark)]
        [InlineData(1024, 720, ThemeMode.Light)]
        [InlineData(1920, 1080, ThemeMode.Dark)]
        [InlineData(1920, 1080, ThemeMode.Light)]
        public void ComposerRightRail_ShareOneRightEdge(double w, double h, ThemeMode mode) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, mode);
                ShowingAt(view, w, h, () => AssertRightRailEdges(view));
            });

        [Theory]
        [InlineData(1024, 720)]
        [InlineData(1920, 1080)]
        public void ComposerRightRail_ShareOneRightEdge_WhenItScrolls(double w, double h) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, ThemeMode.Dark, extraChanges: 6);
                // The scroller gets the height left above the pinned cluster; a short window makes it overflow for real.
                ShowingAt(view, w, Math.Min(h, 600), () =>
                {
                    var scroll = (ScrollViewer)Named(view, "RightRailScroll").Single();
                    Assert.True(scroll.ScrollableHeight > 0, $"the right rail does not scroll (viewport {scroll.ViewportHeight:F1}, extent {scroll.ExtentHeight:F1})");
                    AssertRightRailEdges(view);
                });
            });

        [Theory]
        [InlineData(1024, 720)]
        [InlineData(1920, 1080)]
        public void ComposerLeftRail_ShareOneLeftEdge(double w, double h) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, ThemeMode.Dark);
                ShowingAt(view, w, h, () =>
                {
                    var heading = VisibleNamed(view, "StepLayoutHeading");
                    var tiles = Named(view, "LayoutTile").Where(t => t.IsVisible && t.ActualWidth > 0).ToList();
                    Assert.True(tiles.Count >= 3, $"only {tiles.Count} visible layout tiles");
                    var left = BoundsIn(heading, view).Left;
                    foreach (var t in tiles)
                        Assert.True(Math.Abs(BoundsIn(t, view).Left - left) <= EdgeTolerance, $"tile left {BoundsIn(t, view).Left:F1} vs heading {left:F1}");
                    // the header starts where the left rail starts
                    var lead = (Style)Application.Current.FindResource("Style.Text.Lead");
                    var headerText = FindAll<TextBlock>(view).First(t => t.IsVisible && ReferenceEquals(t.Style, lead));
                    Assert.True(Math.Abs(BoundsIn(headerText, view).Left - left) <= EdgeTolerance, $"header left {BoundsIn(headerText, view).Left:F1} vs rail {left:F1}");
                });
            });

        [Theory]
        [InlineData(1024, 720)]
        [InlineData(1920, 1080)]
        public void ComposerRow_ButtonsAreCentred(double w, double h) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, ThemeMode.Dark);
                ShowingAt(view, w, h, () =>
                {
                    var done = VisibleNamed(view, "DoneButton");
                    var frame = VisibleNamed(view, "CanvasFrame");
                    Assert.True(Math.Abs(BoundsIn(done, view).Right - BoundsIn(frame, view).Right) <= EdgeTolerance,
                        $"Done R={BoundsIn(done, view).Right:F1}, canvas frame R={BoundsIn(frame, view).Right:F1}");

                    double Mid(FrameworkElement e) => BoundsIn(e, view).Top + BoundsIn(e, view).Height / 2;
                    var step2 = VisibleNamed(view, "StepPlaceHeading");
                    Assert.True(Math.Abs(Mid(done) - Mid(step2)) <= EdgeTolerance, $"Done centre {Mid(done):F1} vs Step 2 header {Mid(step2):F1}");
                    var discard = VisibleNamed(view, "DiscardButton");
                    var clear = VisibleNamed(view, "ClearAllButton");
                    Assert.True(Math.Abs(Mid(discard) - Mid(clear)) <= EdgeTolerance, $"Discard centre {Mid(discard):F1} vs Clear all {Mid(clear):F1}");
                    foreach (var b in new[] { done, discard, clear })
                    {
                        var label = FindAll<TextBlock>(b).Single(t => t.IsVisible);
                        Assert.True(Math.Abs(Mid(label) - Mid(b)) <= EdgeTolerance, $"{b.Name} label centre {Mid(label):F1} vs button {Mid(b):F1}");
                    }
                });
            });

        [Fact]
        public void ComposerUndo_AtTheFloor_KeepsTouchHeightAndItsLabel() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, ThemeMode.Dark);
                ShowingAt(view, 1024, 720, () =>
                {
                    var undo = (Button)VisibleNamed(view, "UndoButton");
                    Assert.True(undo.ActualHeight >= ButtonToken("Size.Touch.Min") - 0.01, $"Undo is {undo.ActualHeight:F1} high");
                    var label = FindAll<TextBlock>(undo).Single(t => t.IsVisible);
                    Assert.True(label.ActualHeight <= undo.ActualHeight, $"Undo label {label.ActualHeight:F1} clips in {undo.ActualHeight:F1}");
                    Assert.True(BoundsIn(label, view).Right <= BoundsIn(undo, view).Right + 0.5, "Undo label overflows to the right");
                    var card = VisibleNamed(view, "OnScreenCard");
                    Assert.True(Math.Abs(BoundsIn(undo, view).Right - BoundsIn(card, view).Right) <= EdgeTolerance, $"Undo R={BoundsIn(undo, view).Right:F1}, rail R={BoundsIn(card, view).Right:F1}");
                });
            });
    }
}
