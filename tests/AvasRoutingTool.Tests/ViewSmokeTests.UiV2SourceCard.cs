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
    /// <summary>UI v2 source card (plan task A8): ui:Card frame, tokens, badges, Material icons, the 12 px floor.</summary>
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
                    AssertFooter(view, new[] { "StartStreamButton" }, ("StartStreamButton", "play_arrow")));

                var streaming = UiV2Source();
                streaming.IsStreaming = true;
                SourceCardAt(streaming, view =>
                    AssertFooter(view, new[] { "RefreshStreamButton", "StopStreamButton" },
                        ("RefreshStreamButton", "sync"), ("StopStreamButton", "stop")));
            });

        private static void AssertFooter(SourceCardView view, string[] names, params (string Name, string Kind)[] kinds)
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
            var icons = visible.ToDictionary(b => b.Name, b => FindAll<AvasRoutingTool.Controls.MaterialIcon>(b).Single().Kind);
            foreach (var (name, kind) in kinds) Assert.Equal(kind, icons[name]);
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
                    Assert.Equal("stop", FindAll<AvasRoutingTool.Controls.MaterialIcon>(Named(view, "StopStreamButton").Single()).Single().Kind);
                    AssertVisibleText(view, "30.0 FPS");
                    Assert.Empty(VisibleTexts(view, "Preview standby"));
                });
            });

        [Fact]
        public void SourceCard_StreamingWithNoFrameYet_SaysItIsConnecting_NotABlankPicture() =>
            RunSta(() =>
            {
                var vm = UiV2Source();
                vm.IsStreaming = true;
                SourceCardAt(vm, view =>
                {
                    AssertVisibleText(view, "Connecting preview");
                    AssertVisibleText(view, "Waiting for signal");
                    Assert.Empty(VisibleTexts(view, "Preview standby"));
                });
                var idle = UiV2Source();
                SourceCardAt(idle, view => Assert.Empty(VisibleTexts(view, "Connecting preview")));
            });

        [Fact]
        public void SourceCard_Words_AreSentenceCase_AndPlaceholderIconIsIconL() =>
            RunSta(() =>
                SourceCardAt(UiV2Source(link: "Dual Link"), view =>
                {
                    AssertVisibleText(view, "Preview standby");
                    AssertVisibleText(view, "Preview paused");
                    AssertVisibleText(view, "Dual link");
                    var icon = FindAll<AvasRoutingTool.Controls.MaterialIcon>(view).First(i => i.Kind == "videocam" && i.IsVisible);
                    Assert.Equal((double)Application.Current.FindResource("Size.Icon.L"), icon.ActualWidth, 1);
                    var label = VisibleTexts(view, "Preview standby").Single();
                    Assert.True(label.FontSize >= 13, $"label {label.FontSize}");
                }));

        [Fact]
        public void SourceCard_StopStreamButton_IsTheOutlinedActionBarButton_NotTheRedUnrouteStyle() =>
            RunSta(() =>
            {
                // Stopping a preview is not an error: the Stop control is the outlined action bar button (full-contrast text, 2 px outline), red is for errors only (A12-5)
                var vm = UiV2Source();
                vm.IsStreaming = true;
                SourceCardAt(vm, view =>
                {
                    var stop = (Button)Named(view, "StopStreamButton").Single();
                    Assert.True(stop.IsVisible);
                    Assert.Same(Application.Current.FindResource("Style.Button.ActionBar.Outlined"), stop.Style);
                    Assert.NotSame(Application.Current.FindResource("Style.Button.Unroute"), stop.Style);
                    Assert.True(stop.ActualHeight >= 36 && stop.IsTabStop, $"stop {stop.ActualWidth}x{stop.ActualHeight}");
                    Assert.Equal("stop", FindAll<AvasRoutingTool.Controls.MaterialIcon>(stop).Single().Kind);
                    var word = FindAll<TextBlock>(stop).Single(t => t.Text == "Stop");
                    Assert.True(word.FontSize >= 12, $"Stop word {word.FontSize}px");
                });
            });

        [Theory]
        [InlineData(AvasRoutingTool.Services.ThemeMode.Dark)]
        [InlineData(AvasRoutingTool.Services.ThemeMode.Light)]
        public void SourceCard_StopStreamButton_AtRest_IsOutlined_WithPrimaryText_InBothThemes(AvasRoutingTool.Services.ThemeMode mode) =>
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
                        Assert.Equal(2, body.BorderThickness.Left);
                        Assert.Equal(Visibility.Collapsed, ring.Visibility);
                        var primary = ((System.Windows.Media.SolidColorBrush)Application.Current.FindResource("Brush.Text.Primary")).Color;
                        Assert.Equal(primary, ((System.Windows.Media.SolidColorBrush)stop.Foreground).Color);
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
                    var icon = FindAll<AvasRoutingTool.Controls.MaterialIcon>(badge).Single();
                    Assert.Equal("info", icon.Kind);
                    Assert.Equal((double)Application.Current.FindResource("Size.Icon.S"), icon.Width);
                }));

        [Fact]
        public void SourceCard_DualLinkIsTheOnlyBadge_AndStatusIsAWord_DualLinkShowsOnlyForDualLink() =>
            RunSta(() =>
            {
                SourceCardAt(UiV2Source(link: "Dual Link"), view =>
                {
                    var badges = FindAll<Badge>(view).Where(b => b.IsVisible).ToList();
                    Assert.Single(badges);
                    Assert.Contains(badges, b => FindAll<TextBlock>(b).Any(t => t.Text == "Dual link"));
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

        // Baseline on the unchanged tile (A12-6), measured at 240 px wide (the icon X and the full tile height were re-measured for the 300 x 155 tile of round 3: the icon is still 25 px from the right edge): the info icon's centre in the tile's coordinates,
        // the full tile's natural height (idle and streaming) and the compact tile's natural height.
        private const double InfoBadgeIconCentreX = 275.0;
        // 25.0 since the info icon is Material: a 16 px box, centred in the 50 px header row, instead of the Fluent glyph box (was 25.72).
        private const double InfoBadgeIconCentreY = 25.0;
        // 276.96 since the footer icons are Material (22 px exactly): the Start/Stop row now sits on its 36 px minimum, 1.44 px under the old Fluent glyph height (was 278.4).
        private const double FullTileHeight = 288.96;
        private const double CompactTileHeightAt240 = 81.0;

        [Fact]
        public void SourceCard_InfoBadge_IsAtLeast36By36() =>
            RunSta(() =>
                SourceCardAt(UiV2Source(), view =>
                {
                    var badge = Named(view, "SourceInfoBadge").Single();
                    Assert.True(badge.ActualWidth >= 36 && badge.ActualHeight >= 36, $"info badge is {badge.ActualWidth:F1} x {badge.ActualHeight:F1}");
                }));

        [Fact]
        public void SourceCard_InfoBadge_TakesAPressOnA36By36Area_CentredOnTheIcon() =>
            RunSta(() =>
                SourceCardAt(UiV2Source(), view =>
                {
                    // The 36 x 36 target is centred on the icon; its corners are taken 1 px inside, plus the centre.
                    var badge = Named(view, "SourceInfoBadge").Single();
                    var icon = FindAll<AvasRoutingTool.Controls.MaterialIcon>(badge).Single();
                    var box = BoundsIn(icon, view);
                    double cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2;
                    var spots = new[] { new Point(cx - 17, cy - 17), new Point(cx + 17, cy - 17), new Point(cx - 17, cy + 17), new Point(cx + 17, cy + 17), new Point(cx, cy) };
                    foreach (var spot in spots)
                    {
                        var hit = PressTarget(view, spot);
                        string landsOn = hit == null ? "nothing" : $"{hit.GetType().Name} '{(hit as FrameworkElement)?.Name}'";
                        Assert.True(hit != null && Within(hit, badge), $"a press at ({spot.X:F1},{spot.Y:F1}) lands on {landsOn}, not on the info badge");
                    }
                }));

        [Fact]
        public void SourceCard_InfoBadge_IconCentreDoesNotMove() =>
            RunSta(() =>
                SourceCardAt(UiV2Source(), view =>
                {
                    var icon = FindAll<AvasRoutingTool.Controls.MaterialIcon>(Named(view, "SourceInfoBadge").Single()).Single();
                    var box = BoundsIn(icon, view);
                    Assert.Equal(InfoBadgeIconCentreX, box.X + box.Width / 2, 0.5);
                    Assert.Equal(InfoBadgeIconCentreY, box.Y + box.Height / 2, 0.5);
                }));

        [Fact]
        public void SourceCard_TileHeight_IsTheLargerTileHeight_IdleAndStreaming() =>
            RunSta(() =>
            {
                foreach (var streaming in new[] { false, true })
                {
                    var vm = UiV2Source();
                    vm.IsStreaming = streaming;
                    SourceCardAt(vm, view =>
                    {
                        view.Measure(new Size(300, double.PositiveInfinity));
                        Assert.Equal(FullTileHeight, view.DesiredSize.Height, 0.5);
                    });
                }
            });

        [Fact]
        public void SourceCard_Compact_HasNoInfoBadge_AndItsHeightIsUnchanged() =>
            RunSta(() =>
            {
                var view = new SourceCardView { DataContext = UiV2Source(), IsCompact = true, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
                Showing(view, () =>
                {
                    Assert.False(Named(view, "SourceInfoBadge").Single().IsVisible);
                    view.Measure(new Size(240, double.PositiveInfinity));
                    Assert.Equal(CompactTileHeightAt240, view.DesiredSize.Height, 0.5);
                });
            });

        [Fact]
        public void SourceCard_Width_IsTheStripTileToken() =>
            RunSta(() =>
            {
                // A9: the tile is a fixed strip tile (300 px), not a minimum-width card.
                var view = new SourceCardView();
                Assert.Equal((double)Application.Current.FindResource("Size.Strip.Tile.Width"), view.Width);
                Assert.Equal(300.0, view.Width);
            });
    }
}
