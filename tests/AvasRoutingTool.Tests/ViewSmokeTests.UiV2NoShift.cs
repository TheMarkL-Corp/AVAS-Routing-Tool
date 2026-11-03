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
                    Assert.True(Math.Abs(banner.ActualHeight - headerRow) < 0.5, $"banner {banner.ActualHeight} px, header row {headerRow} px: arming must not change the row");

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

        /// <summary>
        /// The banner keeps its footprint without pulling its Stop button over its own edge: no element inside it may have a
        /// negative margin (the old -2 px margins on Stop drew it across the banner's 2 px ring to keep the banner 48 px tall).
        /// </summary>
        [Fact]
        public void ArmBanner_NoElementInsideHasANegativeMargin() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    rig.Vm.ArmSource(rig.Tx2);
                    Relayout(view);
                    var banner = Named(view, "ArmBanner").Single();
                    Assert.True(banner.IsVisible, "the arm banner is not shown");
                    var inside = FindAll<FrameworkElement>(banner).Prepend(banner).ToList();
                    Assert.True(inside.Count > 5, $"the banner's tree did not load ({inside.Count} elements)");
                    foreach (var el in inside)
                    {
                        var m = el.Margin;
                        Assert.True(m.Left >= 0 && m.Top >= 0 && m.Right >= 0 && m.Bottom >= 0,
                            $"{el.GetType().Name} '{el.Name}' in the arm banner has a negative margin {m}");
                    }
                });
            });

        /// <summary>
        /// The banner's 2 px ring is its own layer (not the Border's BorderThickness, which would take 4 px off the content),
        /// it takes no presses, and it is the Stop button's colour in both themes.
        /// </summary>
        [Fact]
        public void ArmBanner_RingLayer_IsNotHitTestable_AndMatchesTheStopButton_InBothThemes() =>
            RunSta(() =>
            {
                var theme = new AvasRoutingTool.Services.ThemeService();
                var ringColours = new Dictionary<AvasRoutingTool.Services.ThemeMode, System.Windows.Media.Color>();
                try
                {
                    foreach (var mode in new[] { AvasRoutingTool.Services.ThemeMode.Light, AvasRoutingTool.Services.ThemeMode.Dark })
                    {
                        // The rig first: its MainViewModel re-applies the saved (Dark) theme when it is built.
                        var rig = new CopyRig();
                        var view = new VisualRoutingView { DataContext = rig.Vm };
                        Showing(view, () =>
                        {
                            rig.Vm.ArmSource(rig.Tx2);
                            theme.SetTheme(mode);
                            Relayout(view);

                            var banner = Named(view, "ArmBanner").Single();
                            Assert.True(banner.IsVisible, $"{mode}: the arm banner is not shown");
                            var ring = Named(view, "ArmBannerRing").OfType<Border>().Single();
                            Assert.True(ring.IsVisible, $"{mode}: the ring is not shown");
                            Assert.False(ring.IsHitTestVisible, $"{mode}: the ring takes presses away from the banner and Stop");
                            Assert.Equal(new Thickness(2), ring.BorderThickness);
                            Assert.Equal(new Thickness(0), ((Border)banner).BorderThickness);

                            var stop = (Button)Named(view, "StopPlacingButton").Single();
                            var stopColour = ((System.Windows.Media.SolidColorBrush)stop.Background).Color;
                            var ringColour = ((System.Windows.Media.SolidColorBrush)ring.BorderBrush).Color;
                            Assert.Equal(stopColour, ringColour);
                            Assert.Equal(((System.Windows.Media.SolidColorBrush)Application.Current.FindResource("Brush.Border.DropTarget")).Color, ringColour);
                            ringColours[mode] = ringColour;
                        });
                    }
                }
                finally { theme.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark); }
                Assert.NotEqual(ringColours[AvasRoutingTool.Services.ThemeMode.Light], ringColours[AvasRoutingTool.Services.ThemeMode.Dark]);
            });

        /// <summary>The ring is the banner's edge (not an inset line inside its padding) and the content sits 14 px in from it, so the label and Stop do not touch the ring.</summary>
        [Fact]
        public void ArmBanner_RingIsTheBannersEdge_AndTheContentSitsInsideIt() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    rig.Vm.ArmSource(rig.Tx2);
                    Relayout(view);
                    var banner = (FrameworkElement)Named(view, "ArmBanner").Single();
                    var ring = Named(view, "ArmBannerRing").Single();
                    var stop = Named(view, "StopPlacingButton").Single();
                    var label = Named(view, "ArmBannerLabel").Single();
                    var b = BoundsIn(banner, view);
                    var r = BoundsIn(ring, view);
                    Assert.True(Math.Abs(r.Left - b.Left) < 0.5 && Math.Abs(r.Right - b.Right) < 0.5 && Math.Abs(r.Top - b.Top) < 0.5 && Math.Abs(r.Bottom - b.Bottom) < 0.5,
                        $"the ring {r} is not the banner's edge {b}");
                    Assert.True(BoundsIn(label, view).Left - b.Left >= 13.5, $"the label is {BoundsIn(label, view).Left - b.Left:F1} px from the banner's edge");
                    Assert.True(b.Right - BoundsIn(stop, view).Right >= 13.5, $"Stop is {b.Right - BoundsIn(stop, view).Right:F1} px from the banner's edge");
                });
            });

        /// <summary>The Stop button draws its own keyboard focus ring over its outer 2 px, so the ring layer must sit beneath it, or the focus ring is hidden.</summary>
        [Fact]
        public void ArmBanner_StopButton_KeepsItsKeyboardFocusRing() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    rig.Vm.ArmSource(rig.Tx2);
                    Relayout(view);
                    var banner = Named(view, "ArmBanner").Single();
                    var stop = (Button)Named(view, "StopPlacingButton").Single();
                    AssertShowsFocus(stop, banner, "the Stop button in the arm banner");
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
