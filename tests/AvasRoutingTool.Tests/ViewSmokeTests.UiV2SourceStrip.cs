using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>UI v2 source strip (critique M2): dense left-aligned tiles with a 16:9 thumbnail, the toggle footer, the Auto Sources row.</summary>
    public partial class ViewSmokeTests
    {
        /// <summary>The thumbnail: the one shown Border whose child grid holds the live picture (the compact layout's collapsed copy is not shown).</summary>
        private static Border ThumbnailOf(FrameworkElement tile) =>
            FindAll<Border>(tile).Single(b => b.IsVisible && b.Child is Grid g && FindAll<System.Windows.Controls.Image>(g).Count == 1);

        /// <summary>The Sources list's own ScrollViewer (the one that hosts the source ItemsControl).</summary>
        private static ScrollViewer SourcesScrollOf(VisualRoutingView view, MainViewModel vm) =>
            FindAll<ScrollViewer>(view).Single(s => s.Name != "CompactSourcePalette" && s.Content is ItemsControl ic && ReferenceEquals(ic.ItemsSource, vm.Sources));

        [Fact]
        public void Strip_IsCentred_TilesHaveEqualSizeFromTokens() =>
            RunSta(() =>
            {
                var (view, _) = FullHdOverview(sources: 3, displays: 2);
                WithFullHdWindow(view, () =>
                {
                    var tiles = FindAll<SourceCardView>(view).Where(t => t.IsVisible).OrderBy(t => BoundsIn(t, view).Left).ToList();
                    Assert.Equal(3, tiles.Count);
                    foreach (var tile in tiles)
                        Assert.True(Math.Abs(tile.ActualWidth - Token("Size.Strip.Tile.Width")) < 0.5, $"tile is {tile.ActualWidth:F1} px wide, the token is {Token("Size.Strip.Tile.Width")}");
                    Assert.All(tiles, t => Assert.True(Math.Abs(t.ActualHeight - tiles[0].ActualHeight) < 0.5, $"tile heights differ: {t.ActualHeight:F1} and {tiles[0].ActualHeight:F1}"));

                    // A10b: the strip is centred under the header, so the gap on the left equals the gap on the right.
                    var strip = StripPanelOf(view);
                    var leftGap = BoundsIn(tiles[0], strip).Left;
                    var rightGap = strip.ActualWidth - BoundsIn(tiles[^1], strip).Right;
                    Assert.True(Math.Abs(leftGap - rightGap) <= 1, $"the tiles sit {leftGap:F1} px from the left and {rightGap:F1} px from the right: the strip is not centred");

                    for (var i = 1; i < tiles.Count; i++)
                    {
                        var previous = BoundsIn(tiles[i - 1], view);
                        var current = BoundsIn(tiles[i], view);
                        if (Math.Abs(previous.Top - current.Top) < 0.5)
                        {
                            var gap = current.Left - previous.Right;
                            Assert.True(gap >= 8, $"gap between tiles {i} and {i + 1} is {gap:F1} px (D4: at least 8)");
                        }
                    }
                    return 0;
                });
            });

        /// <summary>The strip is at most Size.Strip.Max.Fraction of the overview height, and scrolls beyond that (the rest of the tiles stay realized).</summary>
        private static void AssertCappedAndScrolling(VisualRoutingView view, ScrollViewer scroll, string what)
        {
            var overview = OverviewGrid(view).ActualHeight;
            var cap = Token("Size.Strip.Max.Fraction") * overview;
            Assert.True(scroll.ActualHeight <= cap + 0.5, $"{what}: the strip is {scroll.ActualHeight:F1} px, the cap is {cap:F1} px (the fraction of {overview:F0})");
            Assert.True(scroll.ScrollableHeight > 0.5, $"{what}: the strip does not scroll");
        }

        [Fact]
        public void Strip_WrapsToASecondRow_AndThenScrolls_WithEveryTileRealized() =>
            RunSta(() =>
            {
                var (three, threeVm) = FullHdOverview(sources: 3, displays: 2);
                WithFullHdWindow(three, () =>
                {
                    var scroll = SourcesScrollOf(three, threeVm);
                    Assert.True(scroll.ScrollableHeight <= 0.5, $"3 sources scroll by {scroll.ScrollableHeight:F1} px: one row needs no scroll");
                    Assert.NotEqual(Visibility.Visible, scroll.ComputedVerticalScrollBarVisibility);
                    return 0;
                });

                var (eight, eightVm) = FullHdOverview(sources: 8, displays: 2);
                WithFullHdWindow(eight, () =>
                {
                    var tiles = FindAll<SourceCardView>(eight).Where(c => !c.IsCompact).ToList();
                    Assert.Equal(8, tiles.Count);
                    Assert.Equal(2, tiles.Select(t => Math.Round(BoundsIn(t, eight).Top)).Distinct().Count());
                    AssertCappedAndScrolling(eight, SourcesScrollOf(eight, eightVm), "8 sources");
                    return 0;
                });

                var (many, manyVm) = FullHdOverview(sources: 22, displays: 2);
                WithFullHdWindow(many, () =>
                {
                    // Every card owns a live preview, so every tile is realized (D11), scrolled into view or not.
                    Assert.Equal(22, FindAll<SourceCardView>(many).Count(c => !c.IsCompact));
                    Assert.Empty(FindAll<VirtualizingPanel>(many).Where(p => FindAll<SourceCardView>(p).Any()));
                    AssertCappedAndScrolling(many, SourcesScrollOf(many, manyVm), "22 sources");
                    return 0;
                });
            });

        [Fact]
        public void SourceTile_ThumbnailIs16by9_AndLastFrameStaysAfterStop() =>
            RunSta(() =>
            {
                var vm = UiV2Source();
                vm.PreviewBitmap = Picture();
                vm.IsStreaming = true;
                vm.IsStreaming = false;
                var view = new SourceCardView { DataContext = vm };
                Showing(view, () =>
                {
                    var thumb = ThumbnailOf(view);
                    Assert.True(Math.Abs(thumb.ActualWidth - 214) <= 1 && Math.Abs(thumb.ActualHeight - 120) <= 1,
                        $"thumbnail is {thumb.ActualWidth:F1}x{thumb.ActualHeight:F1}, expected 214x120");
                    Assert.True(Math.Abs(thumb.ActualWidth / thumb.ActualHeight - 16.0 / 9.0) < 0.02, $"thumbnail ratio {thumb.ActualWidth / thumb.ActualHeight:F3}");

                    var image = FindAll<System.Windows.Controls.Image>(view).Single(i => i.IsVisible);
                    Assert.Same(vm.PreviewBitmap, image.Source);
                });
            });

        [Theory]
        [InlineData("Single Link")]
        [InlineData("Dual Link")]
        public void SourceTile_IdleShowsStandbyPlate_WithLinkMode(string link) =>
            RunSta(() =>
            {
                var view = new SourceCardView { DataContext = UiV2Source(link: link) };
                Showing(view, () =>
                {
                    var thumb = ThumbnailOf(view);
                    Assert.Single(FindAll<TextBlock>(thumb).Where(t => t.IsVisible && t.Text == "Preview Standby"));
                    var mode = Assert.Single(FindAll<TextBlock>(thumb).Where(t => t.IsVisible && t.Text == link));
                    Assert.True(mode.FontSize >= 12, $"'{link}' is {mode.FontSize} px");
                    var muted = ((System.Windows.Media.SolidColorBrush)Application.Current.FindResource("Brush.Text.OnPreviewMuted")).Color;
                    Assert.Equal(muted, ((System.Windows.Media.SolidColorBrush)mode.Foreground).Color);
                });
            });

        [Fact]
        public void SourceTile_PreviewToggle_StartsAndStops_AtLeast36_WithEightPxSpacing() =>
            RunSta(() =>
            {
                var vm = UiV2Source();
                var view = new SourceCardView { DataContext = vm };
                Showing(view, () =>
                {
                    var refresh = (Button)Named(view, "RefreshStreamButton").Single();
                    var stop = (Button)Named(view, "StopStreamButton").Single();
                    var start = (Button)Named(view, "StartStreamButton").Single();
                    System.Collections.Generic.List<Button> Shown() =>
                        new[] { refresh, stop, start }.Where(b => b.IsVisible).OrderBy(b => b.TranslatePoint(new Point(0, 0), view).X).ToList();

                    // Idle: Start only (Refresh is offered while streaming).
                    Assert.Equal(new[] { "StartStreamButton" }, Shown().Select(b => b.Name));
                    Assert.Same(vm.StartPreviewCommand, start.Command);
                    Assert.True(vm.StartPreviewCommand.CanExecute(null), "Start is not executable while idle");

                    vm.IsStreaming = true;
                    Relayout(view);
                    Assert.Equal(new[] { "RefreshStreamButton", "StopStreamButton" }, Shown().Select(b => b.Name));

                    // Refresh is icon only: no text, but its name is announced.
                    // The SymbolIcon draws its glyph as a TextBlock: the only text in the button is that glyph, no word.
                    var glyph = FindAll<SymbolIcon>(refresh).Single();
                    Assert.Empty(FindAll<TextBlock>(refresh).Where(t => !string.IsNullOrWhiteSpace(t.Text) && t.Text != glyph.Glyph));
                    Assert.Equal(SymbolRegular.ArrowSync24, glyph.Symbol);
                    Assert.Equal("Refresh preview", AutomationProperties.GetName(refresh));
                    Assert.Same(vm.RefreshPreviewCommand, refresh.Command);
                    Assert.Same(vm.StopPreviewCommand, stop.Command);
                    Assert.True(vm.StopPreviewCommand.CanExecute(null), "Stop is not executable while streaming");

                    var visible = Shown();
                    foreach (var b in visible)
                    {
                        Assert.True(b.ActualHeight >= 36 && b.ActualWidth >= 36, $"{b.Name} is {b.ActualWidth:F0}x{b.ActualHeight:F0}");
                        var r = BoundsIn(b, view);
                        Assert.True(r.Left >= -0.5 && r.Right <= view.ActualWidth + 0.5 && r.Bottom <= view.ActualHeight + 0.5, $"{b.Name} at {r} is outside the card");
                    }
                    for (var i = 1; i < visible.Count; i++)
                    {
                        var gap = BoundsIn(visible[i], view).Left - BoundsIn(visible[i - 1], view).Right;
                        Assert.True(gap >= 8, $"gap {gap:F1} px between {visible[i - 1].Name} and {visible[i].Name}");
                    }
                });
            });

        [Fact]
        public void SourceTile_LongName_Truncates_WithATooltip_AndNeverWidensTheTile() =>
            RunSta(() =>
            {
                var name = "TX_" + new string('L', 40) + "_ENCODER_NORTH_WALL_7";
                var vm = new SourceCardViewModel(new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = name, VendorId = 105, IsTransmitter = true, IsActive = true });
                var view = new SourceCardView { DataContext = vm };
                Showing(view, () =>
                {
                    Assert.True(Math.Abs(view.ActualWidth - Token("Size.Strip.Tile.Width")) < 0.5, $"the tile widened to {view.ActualWidth:F1} px");
                    var label = FindAll<TextBlock>(view).Single(t => t.IsVisible && t.Text == name);
                    Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
                    Assert.Equal(name, label.ToolTip as string);
                    Assert.True(label.ActualWidth > 0, "the name has no width");

                    foreach (FrameworkElement child in ((Grid)label.Parent).Children)
                    {
                        var r = BoundsIn(child, view);
                        Assert.True(r.Left >= -0.5 && r.Right <= view.ActualWidth + 0.5, $"header item {child.GetType().Name} at {r} is outside the tile");
                    }
                });
            });

        [Fact]
        public void Strip_Empty_ShowsNoTiles_AndTheDisplaysKeepTheirRoom() =>
            RunSta(() =>
            {
                var (view, _) = FullHdOverview(sources: 0, displays: 2);
                WithFullHdWindow(view, () =>
                {
                    Assert.Empty(FindAll<SourceCardView>(view));
                    // The Sources count is the first "N discovered" of the overview: a run "0" then " discovered".
                    var counts = FindAll<TextBlock>(view).Where(t => t.IsVisible && t.Inlines.OfType<System.Windows.Documents.Run>().Any(r => r.Text == " discovered")).ToList();
                    Assert.Contains(counts, t => t.Inlines.OfType<System.Windows.Documents.Run>().First().Text == "0");
                    return 0;
                });

                var m = MeasureFullHd(0, 2, displayCards: true);
                Assert.True(m.PictureWidth >= 673 - 0.5 && m.PictureHeight >= 378 - 0.5,
                    $"2 displays with no sources: {m.PictureWidth:F0}x{m.PictureHeight:F0} (the gate is 673x378)");
            });

        [Fact]
        public void Overview_SourcesRow_IsAuto_AndTheAllowlistHasNoRowDefinitionStyle() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    var rows = OverviewGrid(view).RowDefinitions;
                    Assert.True(rows[0].Height.IsAuto, $"overview sources row {rows[0].Height}");
                    Assert.True(rows[2].Height.IsStar && Math.Abs(rows[2].Height.Value - 1) < 0.001, $"displays row {rows[2].Height}");

                    Pump(() => rig.Vm.OpenComposerAsync(rig.Rx3, "matrix2x2"));
                    Relayout(view);
                    Assert.True(rig.Vm.IsComposing);
                    Assert.True(rows[0].Height.IsAuto, $"composing sources row {rows[0].Height}");
                });

                var allow = UiV2Allowlist.Load();
                Assert.DoesNotContain(allow.InlineStyleExceptions, e => e.File == "Views/VisualRoutingView.xaml");
                var json = File.ReadAllText(Path.Combine(UiV2Allowlist.TestsDirectory(), UiV2Allowlist.FileName));
                Assert.DoesNotContain("TargetType=\\\"RowDefinition\\\"", json);
            });

        [Fact]
        public void FullHd_DisplayPictures_GainTheStripHeight() =>
            RunSta(() =>
            {
                var one = MeasureFullHd(3, 1, displayCards: true);
                var two = MeasureFullHd(3, 2, displayCards: true);
                Assert.True(one.PictureWidth >= 673 - 0.5 && one.PictureHeight >= 378 - 0.5,
                    $"1 display: {one.PictureWidth:F0}x{one.PictureHeight:F0} (the gate is 673x378)");
                Assert.True(two.PictureWidth >= 673 - 0.5 && two.PictureHeight >= 378 - 0.5,
                    $"2 displays: {two.PictureWidth:F0}x{two.PictureHeight:F0} (the gate is 673x378)");
            });

        [Fact]
        public void Floor_1008x615_TheStripScrolls_AndDisplaysStayReachable() =>
            RunSta(() =>
            {
                var (view, vm) = FullHdOverview(sources: 3, displays: 2);
                ShowingAt(view, 1008, 615, () =>
                {
                    var sources = SourcesScrollOf(view, vm);
                    AssertCappedAndScrolling(view, sources, "3 sources at the floor");
                    Assert.True(sources.ExtentWidth <= sources.ViewportWidth + 0.5, "the Sources strip scrolls sideways");

                    // The tiles are reachable by scrolling: at the top their top edge shows, at the end their bottom edge does.
                    var tiles = FindAll<SourceCardView>(view).Where(t => t.IsVisible).ToList();
                    Assert.Equal(3, tiles.Count);
                    Assert.All(tiles, t => Assert.True(BoundsIn(t, sources).Top >= -0.5, "a tile's top edge is cut off before scrolling"));
                    sources.ScrollToEnd();
                    Relayout(view);
                    Assert.All(tiles, t => Assert.True(BoundsIn(t, sources).Bottom <= sources.ViewportHeight + 0.5,
                        $"a tile's bottom edge is still below the strip's view after scrolling to its end ({BoundsIn(t, sources).Bottom:F1} > {sources.ViewportHeight:F1})"));

                    var displays = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    Assert.True(displays.ViewportHeight > 0, "the Displays view has no height");
                    Assert.True(displays.ExtentWidth <= displays.ViewportWidth + 0.5, "the Displays view scrolls sideways");
                    var card = FindAll<DisplayCardView>(view).First(c => c.IsVisible);
                    foreach (var name in new[] { "DeviceNameText", "LayoutButton" })
                    {
                        var r = BoundsIn((FrameworkElement)Named(card, name).Single(), displays);
                        Assert.True(r.Top >= -0.5 && r.Bottom <= displays.ViewportHeight + 0.5,
                            $"{name} at {r} is not in view without scrolling ({displays.ViewportHeight:F0} px high)");
                    }
                });
            });
    }
}
