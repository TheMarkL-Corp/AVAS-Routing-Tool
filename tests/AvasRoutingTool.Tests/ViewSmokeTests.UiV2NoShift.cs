using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AvasRoutingTool.Views;
using Xunit;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>UI v2 Main Window round 2 (critique M1): arming a source, a display or a drop choice never moves a card.</summary>
    public partial class ViewSmokeTests
    {
        // The two real content sizes the overview is judged at: a maximized 1920x1080 window and the 1008x615 floor.
        private static readonly (double Width, double Height)[] NoShiftSizes = { (1920, 952), (1008, 615) };

        private sealed record OverviewSnapshot(List<Rect> Cards, double ScrollableHeight, double ViewportHeight, double ExtentHeight, Visibility ScrollBar);

        /// <summary>Hosts the view in a window of exactly this content size, keeps it open while <paramref name="assert"/> runs, closes it after.</summary>
        private static void AtSize(VisualRoutingView view, double width, double height, Action assert)
        {
            var window = new Window { Content = view, Width = width, Height = height, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Left = -10000 };
            window.Show();
            try
            {
                Relayout(view);
                assert();
            }
            finally
            {
                window.Close();
            }
        }

        private static OverviewSnapshot Snapshot(VisualRoutingView view)
        {
            var cards = FindAll<SourceCardView>(view).Where(c => c.IsVisible).Select(c => BoundsIn(c, view))
                .Concat(FindAll<DisplayCardView>(view).Where(c => c.IsVisible).Select(c => BoundsIn(c, view)))
                .ToList();
            var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
            return new OverviewSnapshot(cards, scroll.ScrollableHeight, scroll.ViewportHeight, scroll.ExtentHeight, scroll.ComputedVerticalScrollBarVisibility);
        }

        private static void AssertNothingMoved(OverviewSnapshot before, OverviewSnapshot after, string what)
        {
            Assert.Equal(5, before.Cards.Count); // two sources and three displays: the scenario really has cards to move
            Assert.Equal(before.Cards.Count, after.Cards.Count);
            for (var i = 0; i < before.Cards.Count; i++)
            {
                var a = before.Cards[i];
                var b = after.Cards[i];
                Assert.True(
                    Math.Abs(a.Top - b.Top) < 0.5 && Math.Abs(a.Left - b.Left) < 0.5 &&
                    Math.Abs(a.Width - b.Width) < 0.5 && Math.Abs(a.Height - b.Height) < 0.5,
                    $"{what}: card {i} moved from {a} to {b}");
            }
            Assert.True(Math.Abs(before.ScrollableHeight - after.ScrollableHeight) < 0.5, $"{what}: ScrollableHeight {before.ScrollableHeight} -> {after.ScrollableHeight}");
            Assert.True(Math.Abs(before.ViewportHeight - after.ViewportHeight) < 0.5, $"{what}: ViewportHeight {before.ViewportHeight} -> {after.ViewportHeight}");
            Assert.True(Math.Abs(before.ExtentHeight - after.ExtentHeight) < 0.5, $"{what}: ExtentHeight {before.ExtentHeight} -> {after.ExtentHeight}");
            Assert.Equal(before.ScrollBar, after.ScrollBar);
        }

        [Fact]
        public void Overview_NothingMoves_WhenASourceIsArmed() =>
            RunSta(() =>
            {
                foreach (var (width, height) in NoShiftSizes)
                {
                    var rig = new CopyRig();
                    var view = new VisualRoutingView { DataContext = rig.Vm };
                    AtSize(view, width, height, () =>
                    {
                        var before = Snapshot(view);
                        rig.Vm.ArmSource(rig.Tx2);
                        Relayout(view);
                        Assert.True(rig.Vm.HasArmBanner, $"{width}x{height}: the source did not arm");
                        Assert.True(Named(view, "ArmBanner").Single().IsVisible, $"{width}x{height}: the arm banner is not shown");
                        AssertNothingMoved(before, Snapshot(view), $"{width}x{height} source armed");
                    });
                }
            });

        [Fact]
        public void Overview_NothingMoves_WhenADisplayIsArmedForCopy() =>
            RunSta(() =>
            {
                foreach (var (width, height) in NoShiftSizes)
                {
                    var rig = new CopyRig();
                    var view = new VisualRoutingView { DataContext = rig.Vm };
                    AtSize(view, width, height, () =>
                    {
                        var before = Snapshot(view);
                        Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
                        Relayout(view);
                        Assert.True(rig.Vm.HasArmBanner, $"{width}x{height}: the display did not arm");
                        Assert.True(Named(view, "ArmBanner").Single().IsVisible, $"{width}x{height}: the arm banner is not shown");
                        AssertNothingMoved(before, Snapshot(view), $"{width}x{height} display armed for copy");
                    });
                }
            });

        [Fact]
        public void Overview_NothingMoves_WhenTheDropChoiceOpens() =>
            RunSta(() =>
            {
                foreach (var (width, height) in NoShiftSizes)
                {
                    var rig = new CopyRig();
                    var view = new VisualRoutingView { DataContext = rig.Vm };
                    AtSize(view, width, height, () =>
                    {
                        var before = Snapshot(view);
                        rig.Vm.OpenDropChoice(rig.Rx1, rig.Tx2);
                        Relayout(view);
                        AssertNothingMoved(before, Snapshot(view), $"{width}x{height} drop choice open");
                    });
                }
            });

        [Fact]
        public void ArmBanner_FitsTheHeaderRow_AtTheFloor() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                AtSize(view, 1008, 615, () =>
                {
                    rig.Vm.ArmSource(rig.Tx2);
                    Relayout(view);

                    var banner = Named(view, "ArmBanner").Single();
                    Assert.True(banner.IsVisible, "the arm banner is not shown");
                    var grid = (Grid)banner.Parent;
                    var headerRow = grid.RowDefinitions[0].ActualHeight;
                    var bannerBounds = BoundsIn(banner, grid);
                    Assert.True(bannerBounds.Top >= -0.5 && bannerBounds.Bottom <= headerRow + 0.5,
                        $"banner {bannerBounds} is not inside the header row (height {headerRow})");
                    Assert.True(banner.ActualHeight >= 48, $"banner is {banner.ActualHeight} px");

                    var stop = (Button)Named(view, "StopPlacingButton").Single();
                    Assert.True(stop.ActualHeight >= 48, $"Stop is {stop.ActualHeight} px");
                    var stopBounds = BoundsIn(stop, grid);
                    Assert.True(stopBounds.Top >= bannerBounds.Top - 0.5 && stopBounds.Bottom <= bannerBounds.Bottom + 0.5,
                        $"Stop {stopBounds} is outside the banner {bannerBounds}");

                    var label = (TextBlock)Named(view, "ArmBannerLabel").Single();
                    Assert.True(label.IsVisible, "the arm label is not shown");
                    Assert.True(label.ActualWidth > 0, "the arm label has no width");
                    Assert.True(label.ActualHeight >= label.DesiredSize.Height - 0.5,
                        $"the arm label is clipped: {label.ActualHeight} px tall, wants {label.DesiredSize.Height}");
                    var labelBounds = BoundsIn(label, grid);
                    Assert.True(labelBounds.Top >= bannerBounds.Top - 0.5 && labelBounds.Bottom <= bannerBounds.Bottom + 0.5,
                        $"the arm label {labelBounds} overflows the banner {bannerBounds}");
                    Assert.True(label.FontSize >= 14, $"the arm label is {label.FontSize} px");
                    Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(label));
                });
            });

        [Fact]
        public void DisplaysHeaderRow_HasTheSameHeight_IdleAndArmed() =>
            RunSta(() =>
            {
                foreach (var (width, height) in NoShiftSizes)
                {
                    var rig = new CopyRig();
                    var view = new VisualRoutingView { DataContext = rig.Vm };
                    AtSize(view, width, height, () =>
                    {
                        var grid = (Grid)((ScrollViewer)Named(view, "DisplaysScroll").Single()).Parent;
                        var idle = grid.RowDefinitions[0].ActualHeight;
                        rig.Vm.ArmSource(rig.Tx2);
                        Relayout(view);
                        var armed = grid.RowDefinitions[0].ActualHeight;
                        Assert.True(Math.Abs(idle - armed) < 0.5, $"{width}x{height}: the Displays header row went from {idle} to {armed} when a source was armed");
                        var token = (double)Application.Current.FindResource("Size.Section.Header.Height");
                        Assert.True(idle >= token - 0.5, $"{width}x{height}: the Displays header row is {idle} px, below the {token} px token");
                    });
                }
            });
    }
}
