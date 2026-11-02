using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>UI v2 source card (plan task A8): ui:Card frame, tokens, badges, SymbolIcons, the 12 px floor.</summary>
    public partial class ViewSmokeTests
    {
        private static SourceCardViewModel UiV2Source(bool online = true, string link = "Single Link", bool avas = true) =>
            new(new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = "TX2", VendorId = avas ? 105 : 192, IsTransmitter = true, IsActive = online, LinkMode = link });

        /// <summary>Hosts the tile at its own size: the strip's fixed width (Size.Strip.Tile.Width) and its natural height.</summary>
        private static void SourceCardAt(SourceCardViewModel vm, Action<SourceCardView> assert)
        {
            var view = new SourceCardView { DataContext = vm };
            Showing(view, () => assert(view));
        }

        [Fact]
        public void SourceCard_Frame_IsAnUiCard_ThatFillsTheCard_AndTheSixteenByNinePictureStaysInside() =>
            RunSta(() =>
                SourceCardAt(UiV2Source(), view =>
                {
                    var frame = Named(view, "SourceCardFrame").Single();
                    Assert.IsType<Card>(frame);
                    Assert.Same(Application.Current.FindResource("Style.Card.Source"), frame.Style);
                    Assert.Equal(view.ActualWidth, frame.ActualWidth, 1);
                    Assert.Equal(view.ActualHeight, frame.ActualHeight, 1);

                    var picture = FindAll<Border>(view).First(b => b.Child is Grid g && FindAll<System.Windows.Controls.Image>(g).Count == 1);
                    Assert.True(picture.ActualWidth > 200 && picture.ActualHeight > 100, $"picture {picture.ActualWidth}x{picture.ActualHeight}");
                    Assert.True(picture.ActualWidth <= frame.ActualWidth && picture.ActualHeight < frame.ActualHeight);
                }));

        [Fact]
        public void SourceCard_FooterControls_AreAtLeast36High_WithAtLeast8pxBetweenThem_AndVectorIcons() =>
            RunSta(() =>
            {
                // One preview control per state: idle shows Start only; streaming shows Refresh (icon only) and Stop.
                SourceCardAt(UiV2Source(), view =>
                    AssertFooter(view, new[] { "StartStreamButton" }, ("StartStreamButton", SymbolRegular.Play24)));

                var streaming = UiV2Source();
                streaming.IsStreaming = true;
                SourceCardAt(streaming, view =>
                    AssertFooter(view, new[] { "RefreshStreamButton", "StopStreamButton" },
                        ("RefreshStreamButton", SymbolRegular.ArrowSync24), ("StopStreamButton", SymbolRegular.Stop24)));
            });

        private static void AssertFooter(SourceCardView view, string[] names, params (string Name, SymbolRegular Symbol)[] symbols)
        {
            var visible = new[] { "RefreshStreamButton", "StartStreamButton", "StopStreamButton" }
                .Select(n => (Button)Named(view, n).Single()).Where(b => b.IsVisible).OrderBy(b => b.TranslatePoint(new Point(0, 0), view).X).ToList();
            Assert.Equal(names, visible.Select(b => b.Name));
            Assert.All(visible, b => Assert.True(b.ActualHeight >= 36 && b.IsTabStop));
            for (var i = 1; i < visible.Count; i++)
            {
                var gap = visible[i].TranslatePoint(new Point(0, 0), view).X - visible[i - 1].TranslatePoint(new Point(visible[i - 1].ActualWidth, 0), view).X;
                Assert.True(gap >= 8, $"gap {gap}");
            }
            var icons = visible.ToDictionary(b => b.Name, b => FindAll<SymbolIcon>(b).Single().Symbol);
            foreach (var (name, symbol) in symbols) Assert.Equal(symbol, icons[name]);
            Assert.All(visible.SelectMany(b => FindAll<TextBlock>(b)).Where(t => !string.IsNullOrWhiteSpace(t.Text) && t.Text.Length > 1),
                t => { Assert.True(t.FontSize >= 12); Assert.DoesNotContain(t.Text, ch => ch >= '←' && ch <= '➿'); });
        }

        [Fact]
        public void SourceCard_StreamingShowsStop_AndTheFrameRate() =>
            RunSta(() =>
            {
                var vm = UiV2Source();
                vm.IsStreaming = true;
                vm.Fps = 30;
                SourceCardAt(vm, view =>
                {
                    Assert.True(Named(view, "StopStreamButton").Single().IsVisible);
                    Assert.False(Named(view, "StartStreamButton").Single().IsVisible);
                    Assert.Equal(SymbolRegular.Stop24, FindAll<SymbolIcon>(Named(view, "StopStreamButton").Single()).Single().Symbol);
                    AssertVisibleText(view, "30.0 FPS");
                    Assert.Empty(VisibleTexts(view, "Preview Standby"));
                });
            });

        [Fact]
        public void SourceCard_StopStreamButton_IsTheGhostButton_NotTheRedUnrouteStyle() =>
            RunSta(() =>
            {
                // Stopping a preview is not an error: the Stop control is the quiet ghost button, red is for errors only (A12-5)
                var vm = UiV2Source();
                vm.IsStreaming = true;
                SourceCardAt(vm, view =>
                {
                    var stop = (Button)Named(view, "StopStreamButton").Single();
                    Assert.True(stop.IsVisible);
                    Assert.Same(Application.Current.FindResource("Style.Button.Ghost"), stop.Style);
                    Assert.NotSame(Application.Current.FindResource("Style.Button.Unroute"), stop.Style);
                    Assert.True(stop.ActualHeight >= 36 && stop.IsTabStop, $"stop {stop.ActualWidth}x{stop.ActualHeight}");
                    Assert.Equal(SymbolRegular.Stop24, FindAll<SymbolIcon>(stop).Single().Symbol);
                    var word = FindAll<TextBlock>(stop).Single(t => t.Text == "Stop");
                    Assert.True(word.FontSize >= 12, $"Stop word {word.FontSize}px");
                });
            });

        [Theory]
        [InlineData(AvasRoutingTool.Services.ThemeMode.Dark)]
        [InlineData(AvasRoutingTool.Services.ThemeMode.Light)]
        public void SourceCard_StopStreamButton_AtRest_IsTransparent_WithSecondaryText_InBothThemes(AvasRoutingTool.Services.ThemeMode mode) =>
            RunSta(() =>
            {
                var theme = new AvasRoutingTool.Services.ThemeService();
                try
                {
                    theme.SetTheme(mode);
                    var vm = UiV2Source();
                    vm.IsStreaming = true;
                    SourceCardAt(vm, view =>
                    {
                        var stop = (Button)Named(view, "StopStreamButton").Single();
                        var ring = (Border)stop.Template.FindName("FocusRing", stop);
                        var body = FindAll<Border>(stop).First(b => !ReferenceEquals(b, ring));
                        Assert.True(body.Background is null || ((System.Windows.Media.SolidColorBrush)body.Background).Color.A == 0, $"{mode}: stop body background {body.Background}");
                        Assert.Equal(Visibility.Collapsed, ring.Visibility);
                        var secondary = ((System.Windows.Media.SolidColorBrush)Application.Current.FindResource("Brush.Text.Secondary")).Color;
                        Assert.Equal(secondary, ((System.Windows.Media.SolidColorBrush)stop.Foreground).Color);
                    });
                }
                finally { theme.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark); }
            });

        [Fact]
        public void SourceCard_InfoBadge_IsAFocusableInfoIcon_WithTheSameNameAndTooltip() =>
            RunSta(() =>
                SourceCardAt(UiV2Source(), view =>
                {
                    var badge = Named(view, "SourceInfoBadge").Single();
                    Assert.True(badge.Focusable && System.Windows.Input.KeyboardNavigation.GetIsTabStop(badge));
                    Assert.Equal("Device network telemetry: IP, MAC, multicast", System.Windows.Automation.AutomationProperties.GetName(badge));
                    Assert.IsType<TextBlock>(badge.ToolTip);
                    var icon = FindAll<SymbolIcon>(badge).Single();
                    Assert.Equal(SymbolRegular.Info24, icon.Symbol);
                    Assert.False(string.IsNullOrEmpty(icon.Glyph));
                    Assert.Equal((double)Application.Current.FindResource("Size.Icon.S"), icon.FontSize);
                }));

        [Fact]
        public void SourceCard_DualLinkIsTheOnlyBadge_AndStatusIsAWord_DualLinkShowsOnlyForDualLink() =>
            RunSta(() =>
            {
                SourceCardAt(UiV2Source(link: "Dual Link"), view =>
                {
                    var badges = FindAll<Badge>(view).Where(b => b.IsVisible).ToList();
                    Assert.Single(badges);
                    Assert.Contains(badges, b => FindAll<TextBlock>(b).Any(t => t.Text == "DUAL LINK"));
                    AssertVisibleText(view, "Online");
                    Assert.Empty(VisibleTexts(view, "ONLINE"));
                });
                SourceCardAt(UiV2Source(online: false), view =>
                {
                    Assert.Empty(FindAll<Badge>(view).Where(b => b.IsVisible));
                    AssertVisibleText(view, "Offline");
                    Assert.Empty(VisibleTexts(view, "OFFLINE"));
                    var dot = FindAll<System.Windows.Shapes.Ellipse>(view).First(e => e.Style == Application.Current.FindResource("Style.StatusDot.Small"));
                    Assert.Equal((double)Application.Current.FindResource("Size.Dot.S"), dot.ActualWidth);
                });
            });

        [Fact]
        public void SourceCard_AllVisibleText_IsAtLeast12px_AndPlaceholdersUseTheIconToken() =>
            RunSta(() =>
            {
                var vm = UiV2Source(avas: false);
                vm.IsArmed = true;
                vm.UsedInBlocks = 2;
                SourceCardAt(vm, view =>
                {
                    foreach (var tb in FindAll<TextBlock>(view).Where(t => t.IsVisible && !string.IsNullOrWhiteSpace(t.Text) && t.TemplatedParent == null && FindAll<SymbolIcon>(view).All(i => i.Glyph != t.Text)))
                        Assert.True(tb.FontSize >= 12, $"'{tb.Text}' is {tb.FontSize}px");
                    AssertVisibleText(view, "Preview not supported");
                    AssertVisibleText(view, "Placing");
                    AssertVisibleText(view, "in 2 blocks");
                    Assert.Empty(VisibleTexts(view, "TAP OR DRAG"));
                    var placeholder = FindAll<System.Windows.Shapes.Path>(view).First(p => p.IsVisible);
                    Assert.Equal((double)Application.Current.FindResource("Size.Icon.M"), placeholder.ActualWidth);
                });
            });

        [Fact]
        public void SourceCard_HasNoTapHintChip() =>
            RunSta(() =>
            {
                // The Sources section header says "Tap a source, then tap a display"; the card itself says nothing more, idle or armed.
                SourceCardAt(UiV2Source(), view => Assert.Empty(VisibleTexts(view, "TAP OR DRAG")));
                var armed = UiV2Source();
                armed.IsArmed = true;
                SourceCardAt(armed, view => Assert.Empty(VisibleTexts(view, "TAP OR DRAG")));
            });

        [Fact]
        public void SourceCard_Width_IsTheStripTileToken() =>
            RunSta(() =>
            {
                // A9: the tile is a fixed strip tile (240 px), not a minimum-width card.
                var view = new SourceCardView();
                Assert.Equal((double)Application.Current.FindResource("Size.Strip.Tile.Width"), view.Width);
                Assert.Equal(240.0, view.Width);
            });
    }
}
