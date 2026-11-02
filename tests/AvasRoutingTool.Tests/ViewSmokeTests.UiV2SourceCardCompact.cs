using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Services;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// UI v2 source tile, compact density (plan task A11): <c>SourceCardView.IsCompact</c> gives the Composer palette a 240 px tile
    /// with a 112 x 63 thumbnail, the device name, the status word and the chips. The default (not compact) is the full tile as before.
    /// </summary>
    public partial class ViewSmokeTests
    {
        /// <summary>Hosts the tile at its natural height (top-aligned, as a strip row hosts it), in the given density.</summary>
        private static void TileAt(SourceCardViewModel vm, bool compact, Action<SourceCardView> assert)
        {
            var view = new SourceCardView
            {
                DataContext = vm,
                IsCompact = compact,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            Showing(view, () => assert(view));
        }

        [Fact]
        public void SourceCard_ByDefault_IsNotCompact_AndShowsOnlyTheFullLayout() =>
            RunSta(() =>
            {
                Assert.False((bool)new SourceCardView().GetValue(SourceCardView.IsCompactProperty));
                TileAt(UiV2Source(), false, view =>
                {
                    Assert.False(view.IsCompact);
                    Assert.True(Named(view, "FullLayout").Single().IsVisible);
                    Assert.False(Named(view, "CompactLayout").Single().IsVisible);
                    foreach (var name in new[] { "StartStreamButton", "StopStreamButton", "RefreshStreamButton" })
                        Assert.Single(Named(view, name));
                    Assert.True(view.ActualHeight > 200, $"the full tile is {view.ActualHeight:F1} high");
                });
            });

        [Fact]
        public void SourceCard_Compact_IsAShortTileAtTwoForty_WithNoStreamControlsNoDualLinkTagNoInfoBadge() =>
            RunSta(() =>
            {
                var vm = UiV2Source(link: "Dual Link");
                vm.IsStreaming = true;
                vm.Fps = 30;
                TileAt(vm, true, view =>
                {
                    Assert.True(view.ActualHeight <= 110 && view.ActualHeight > 60, $"the compact tile is {view.ActualHeight:F1} high");
                    Assert.Equal(240.0, view.ActualWidth, 0.5);
                    Assert.False(Named(view, "FullLayout").Single().IsVisible);
                    Assert.True(Named(view, "CompactLayout").Single().IsVisible);
                    foreach (var name in new[] { "StartStreamButton", "StopStreamButton", "RefreshStreamButton" })
                        Assert.False(Named(view, name).Single().IsVisible, $"{name} is shown in the compact tile");
                    Assert.Empty(VisibleTexts(view, "DUAL LINK"));
                    Assert.Empty(FindAll<Badge>(view).Where(b => b.IsVisible));
                    Assert.False(Named(view, "SourceInfoBadge").Single().IsVisible);
                    Assert.False(string.IsNullOrEmpty(vm.StreamStatusText));
                    Assert.Empty(FindAll<TextBlock>(view).Where(t => t.IsVisible && t.Text == vm.StreamStatusText));
                });
            });

        [Fact]
        public void SourceCard_Compact_ThumbnailIs112By63_WithTheNameAndTheOnlineWord() =>
            RunSta(() =>
            {
                TileAt(UiV2Source(), true, view =>
                {
                    var thumb = (System.Windows.Controls.Border)Named(view, "CompactThumb").Single();
                    Assert.True(thumb.IsVisible);
                    Assert.Equal(112, thumb.ActualWidth, 0.5);
                    Assert.Equal(63, thumb.ActualHeight, 0.5);
                    double ratio = thumb.ActualWidth / thumb.ActualHeight;
                    Assert.InRange(ratio, 16.0 / 9 - 0.01, 16.0 / 9 + 0.01);

                    var name = (TextBlock)Named(view, "CompactName").Single();
                    Assert.True(name.IsVisible);
                    Assert.Equal("TX2", name.Text);
                    Assert.Equal("TX2", name.ToolTip as string);
                    Assert.True(name.FontSize >= 12, $"the name is {name.FontSize} px");

                    var dot = FindAll<System.Windows.Shapes.Ellipse>(view).Single(e => e.IsVisible);
                    Assert.Same(Application.Current.FindResource("Style.StatusDot.Small"), dot.Style);
                    Assert.Equal(PaletteColor("Brush.Status.Online"), ColorOf(dot.Fill));
                    var word = (TextBlock)Named(view, "CompactOnlineWord").Single();
                    Assert.True(word.IsVisible);
                    Assert.Equal("Online", word.Text);
                    Assert.False(Named(view, "CompactOfflineWord").Single().IsVisible);
                    Assert.True(BoundsIn(word, view).Left >= BoundsIn(dot, view).Right - 0.5, "the word is not beside the dot");
                });

                TileAt(UiV2Source(online: false), true, view =>
                {
                    var dot = FindAll<System.Windows.Shapes.Ellipse>(view).Single(e => e.IsVisible);
                    Assert.Equal(PaletteColor("Brush.Status.Offline"), ColorOf(dot.Fill));
                    var word = (TextBlock)Named(view, "CompactOfflineWord").Single();
                    Assert.True(word.IsVisible);
                    Assert.Equal("Offline", word.Text);
                    Assert.False(Named(view, "CompactOnlineWord").Single().IsVisible);
                });
            });

        [Fact]
        public void SourceCard_Compact_ShowsTheStagedCountAndPlacing_AndStaysShort() =>
            RunSta(() =>
            {
                var staged = UiV2Source();
                staged.UsedInBlocks = 2;
                TileAt(staged, true, view =>
                {
                    var badge = (System.Windows.Controls.Border)Named(view, "CompactUsedInBadge").Single();
                    Assert.True(badge.IsVisible);
                    Assert.Equal("in 2 blocks", FindAll<TextBlock>(badge).Single().Text);
                    Assert.Empty(VisibleTexts(view, "Placing"));
                    Assert.False(Named(view, "ArmedRing").Single().IsVisible);
                    Assert.True(view.ActualHeight <= 110, $"the compact tile is {view.ActualHeight:F1} high");
                });

                var none = UiV2Source();
                none.UsedInBlocks = 0;
                TileAt(none, true, view => Assert.False(Named(view, "CompactUsedInBadge").Single().IsVisible));

                var armed = UiV2Source();
                armed.UsedInBlocks = 2;
                armed.IsArmed = true;
                TileAt(armed, true, view =>
                {
                    AssertVisibleText(view, "Placing");
                    AssertVisibleText(view, "in 2 blocks");
                    Assert.True(Named(view, "ArmedRing").Single().IsVisible);
                    Assert.True(view.ActualHeight <= 110, $"armed and staged, the compact tile is {view.ActualHeight:F1} high");
                });
            });

        [Fact]
        public void SourceCard_Compact_AllVisibleText_IsAtLeast12px_AndNonAvasShowsNoPreview() =>
            RunSta(() =>
            {
                var vm = UiV2Source(avas: false);
                vm.IsArmed = true;
                vm.UsedInBlocks = 2;
                TileAt(vm, true, view =>
                {
                    var texts = FindAll<TextBlock>(view)
                        .Where(t => t.IsVisible && !string.IsNullOrWhiteSpace(t.Text) && t.TemplatedParent == null
                                    && FindAll<SymbolIcon>(view).All(i => i.Glyph != t.Text))
                        .ToList();
                    Assert.NotEmpty(texts);
                    foreach (var tb in texts)
                        Assert.True(tb.FontSize >= 12, $"'{tb.Text}' is {tb.FontSize} px");
                    AssertVisibleText(view, "No preview");
                    AssertVisibleText(view, "in 2 blocks");
                    AssertVisibleText(view, "Placing");
                    Assert.Empty(VisibleTexts(view, "Preview not supported"));
                    Assert.Empty(VisibleTexts(view, "Non-AVAS"));
                });
            });

        [Fact]
        public void SourceCard_Compact_StandbyPlaceholderShowsForAnIdleAvasSource() =>
            RunSta(() =>
                TileAt(UiV2Source(), true, view =>
                {
                    AssertVisibleText(view, "Standby");
                    Assert.Empty(VisibleTexts(view, "No preview"));
                }));

        [Fact]
        public void SourceCard_Compact_WholeTileIsTheTapTarget_NoInnerButtonOrFocusStop() =>
            RunSta(() =>
            {
                var vm = UiV2Source(link: "Dual Link");
                vm.UsedInBlocks = 2;
                vm.IsArmed = true;
                TileAt(vm, true, view =>
                {
                    double w = view.ActualWidth, h = view.ActualHeight;
                    var thumb = Named(view, "CompactThumb").Single();
                    var name = Named(view, "CompactName").Single();
                    var points = new[]
                    {
                        new Point(4, 4), new Point(w - 4, 4), new Point(4, h - 4), new Point(w - 4, h - 4), new Point(w / 2, h / 2),
                        thumb.TranslatePoint(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2), view),
                        name.TranslatePoint(new Point(name.ActualWidth / 2, name.ActualHeight / 2), view),
                    };
                    foreach (var p in points)
                    {
                        var hit = VisualTreeHelper.HitTest(view, p)?.VisualHit as DependencyObject;
                        Assert.True(hit != null && Within(hit, view), $"the tile does not take the press at {p}");
                    }

                    var stops = FindAll<UIElement>(view)
                        .Where(e => !ReferenceEquals(e, view) && e.IsVisible && (e is Button || e.Focusable))
                        .Select(e => $"{e.GetType().Name} {(e as FrameworkElement)?.Name}".Trim())
                        .ToList();
                    Assert.Empty(stops);
                });
            });

        [Fact]
        public void SourceCard_Compact_InBothThemes_StaysShort_AndTheNameIsThePrimaryText() =>
            RunSta(() =>
            {
                var theme = new ThemeService();
                try
                {
                    foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light })
                    {
                        theme.SetTheme(mode);
                        TileAt(UiV2Source(), true, view =>
                        {
                            Assert.True(view.ActualHeight <= 110, $"{mode}: the compact tile is {view.ActualHeight:F1} high");
                            var name = (TextBlock)Named(view, "CompactName").Single();
                            Assert.Equal(PaletteColor("Brush.Text.Primary"), ColorOf(name.Foreground));
                        });
                    }
                }
                finally { theme.SetTheme(ThemeMode.Dark); }
            });

        [Fact]
        public void SourceCard_Compact_CardPadding_IsTheCompactToken_DefaultIsTwelve() =>
            RunSta(() =>
            {
                TileAt(UiV2Source(), false, view =>
                    Assert.Equal(new Thickness(12), ((Card)Named(view, "SourceCardFrame").Single()).Padding));
                TileAt(UiV2Source(), true, view =>
                {
                    var padding = ((Card)Named(view, "SourceCardFrame").Single()).Padding;
                    Assert.Equal((Thickness)Application.Current.FindResource("Size.Strip.Compact.Padding"), padding);
                    Assert.Equal(new Thickness(8), padding);
                });
            });

        [Fact]
        public void SourceCard_Compact_Tokens_AreTheThumbnailAndThePadding() =>
            RunSta(() =>
            {
                Assert.Equal(112.0, Token("Size.Strip.Compact.Thumb.Width"));
                Assert.Equal(63.0, Token("Size.Strip.Compact.Thumb.Height"));
                Assert.Equal(new Thickness(8), (Thickness)Application.Current.FindResource("Size.Strip.Compact.Padding"));
            });

        [Fact]
        public void SourceCard_Compact_WhenTheHostStretchesIt_KeepsTheThumbnailAndTheHeight() =>
            RunSta(() =>
            {
                var vm = UiV2Source();
                vm.UsedInBlocks = 2;
                vm.IsArmed = true;
                var view = new SourceCardView
                {
                    DataContext = vm,
                    IsCompact = true,
                    Width = double.NaN,
                    VerticalAlignment = VerticalAlignment.Top,
                };
                var host = new System.Windows.Controls.Border { Width = 340, Child = view };
                Showing(host, () =>
                {
                    Assert.Equal(340, view.ActualWidth, 0.5);
                    Assert.True(view.ActualHeight <= 110, $"stretched, the compact tile is {view.ActualHeight:F1} high");
                    var thumb = (System.Windows.Controls.Border)Named(view, "CompactThumb").Single();
                    Assert.Equal(112, thumb.ActualWidth, 0.5);
                    Assert.Equal(63, thumb.ActualHeight, 0.5);
                });
            });
    }
}
