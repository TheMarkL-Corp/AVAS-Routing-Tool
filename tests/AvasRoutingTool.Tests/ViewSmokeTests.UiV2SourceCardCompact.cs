using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Sdvoe;
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
                    Assert.Empty(VisibleTexts(view, "Dual link"));
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
                    Assert.Null(name.ToolTip);
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

        // ---- Task A12-7: the name wraps to two lines, the tile's tooltip is the full name, Placing sits on the thumbnail ----

        private static SourceCardViewModel NamedSource(string name, bool online = true) =>
            new(new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = name, VendorId = 105, IsTransmitter = true, IsActive = online, LinkMode = "Single Link" });

        /// <summary>The worst case the height budget is set for: a long name (two lines), armed, staged in 2 blocks, offline.</summary>
        private static SourceCardViewModel WorstCase()
        {
            var vm = NamedSource("AVAS223_Operating_Room_Wall_TX2_B", online: false);
            vm.UsedInBlocks = 2;
            vm.IsArmed = true;
            return vm;
        }

        /// <summary>How many lines the text takes at the TextBlock's own width, by WPF's wrap rules (FormattedText, same font and width).</summary>
        private static int WrappedLines(TextBlock tb)
        {
            double lineHeight = Token("Size.Strip.Compact.Name.LineHeight");
            var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
            var text = new FormattedText(tb.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, tb.FontSize,
                                         Brushes.Black, VisualTreeHelper.GetDpi(tb).PixelsPerDip)
            {
                MaxTextWidth = tb.ActualWidth,
                LineHeight = lineHeight,
            };
            return (int)Math.Round(text.Height / lineHeight);
        }

        [Fact]
        public void SourceCard_Compact_ALongName_WrapsToTwoLines_AndTheNameIsCappedAtTwo() =>
            RunSta(() =>
            {
                foreach (var longName in new[] { "TX1_c07bb_SFPA-Transmitter-Rack-12", "AVAS223_Operating_Room_Wall_TX2_B" })
                {
                    TileAt(NamedSource(longName), true, view =>
                    {
                        var name = (TextBlock)Named(view, "CompactName").Single();
                        Assert.Equal(longName, name.Text);
                        Assert.Equal(TextWrapping.Wrap, name.TextWrapping);
                        Assert.Equal(LineStackingStrategy.BlockLineHeight, name.LineStackingStrategy);
                        double lineHeight = Token("Size.Strip.Compact.Name.LineHeight");
                        double maxHeight = Token("Size.Strip.Compact.Name.MaxHeight");
                        Assert.Equal(2 * lineHeight, maxHeight, 0.5);
                        Assert.Equal(lineHeight, name.LineHeight, 0.5);
                        Assert.Equal(maxHeight, name.MaxHeight, 0.5);
                        Assert.True(name.ActualHeight > lineHeight + 0.5, $"'{longName}' is {name.ActualHeight:F1} high: it did not wrap");
                        Assert.True(name.ActualHeight <= maxHeight + 0.5, $"'{longName}' is {name.ActualHeight:F1} high: over two lines");
                    });
                }
            });

        [Fact]
        public void SourceCard_Compact_ATwoLineName_IsFullyVisible_SoItsSuffixIsNotCut() =>
            RunSta(() => TileAt(NamedSource("TX1_c07bb_SFPA-Rack-12"), true, view =>
            {
                var name = (TextBlock)Named(view, "CompactName").Single();
                double lineHeight = Token("Size.Strip.Compact.Name.LineHeight");
                int lines = WrappedLines(name);
                Assert.Equal(2, lines);
                Assert.True(lines * lineHeight <= name.ActualHeight + 0.5,
                    $"the name needs {lines * lineHeight} px and its box is {name.ActualHeight:F1} px: its end is cut");
            }));

        [Fact]
        public void SourceCard_Compact_AShortName_StaysOneLine_AndTheTileIsStill81High() =>
            RunSta(() => TileAt(NamedSource("TX2"), true, view =>
            {
                var name = (TextBlock)Named(view, "CompactName").Single();
                Assert.Equal(1, WrappedLines(name));
                Assert.Equal(Token("Size.Strip.Compact.Name.LineHeight"), name.ActualHeight, 0.5);
                Assert.Equal(81, view.ActualHeight, 0.5);
            }));

        [Fact]
        public void SourceCard_Compact_WorstCase_IsAtMost110High_AndTheThumbnailStays112By63() =>
            RunSta(() => TileAt(WorstCase(), true, view =>
            {
                Assert.True(view.ActualHeight <= 110, $"the worst-case compact tile is {view.ActualHeight:F1} high");
                var thumb = (System.Windows.Controls.Border)Named(view, "CompactThumb").Single();
                Assert.Equal(112, thumb.ActualWidth, 0.5);
                Assert.Equal(63, thumb.ActualHeight, 0.5);
                var name = (TextBlock)Named(view, "CompactName").Single();
                Assert.Equal(Token("Size.Strip.Compact.Name.MaxHeight"), name.ActualHeight, 0.5);
            }));

        [Fact]
        public void SourceCard_Compact_WorstCase_StaysAtMost110High_InBothThemes() =>
            RunSta(() =>
            {
                var theme = new ThemeService();
                try
                {
                    foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light })
                    {
                        theme.SetTheme(mode);
                        TileAt(WorstCase(), true, view =>
                        {
                            Assert.True(view.ActualHeight <= 110, $"{mode}: the worst-case compact tile is {view.ActualHeight:F1} high");
                            var thumb = (System.Windows.Controls.Border)Named(view, "CompactThumb").Single();
                            Assert.Equal(112, thumb.ActualWidth, 0.5);
                            Assert.Equal(63, thumb.ActualHeight, 0.5);
                            Assert.True(Named(view, "ArmedRing").Single().IsVisible);
                        });
                    }
                }
                finally { theme.SetTheme(ThemeMode.Dark); }
            });

        [Fact]
        public void SourceCard_Compact_TheWholeTileCarriesTheFullName_AsItsTooltip_AndIsHitTestedAsOne() =>
            RunSta(() =>
            {
                const string full = "AVAS223_Operating_Room_Wall_TX2_B";
                var vm = NamedSource(full);
                vm.UsedInBlocks = 2;
                vm.IsArmed = true;
                TileAt(vm, true, view =>
                {
                    var layout = (Grid)Named(view, "CompactLayout").Single();
                    Assert.Equal(full, layout.ToolTip as string);
                    Assert.IsType<SolidColorBrush>(layout.Background);
                    Assert.Equal(0, ((SolidColorBrush)layout.Background).Color.A);

                    var name = (TextBlock)Named(view, "CompactName").Single();
                    Assert.Null(name.ToolTip);

                    var thumb = Named(view, "CompactThumb").Single();
                    var points = new[]
                    {
                        thumb.TranslatePoint(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2), view),
                        name.TranslatePoint(new Point(name.ActualWidth / 2, name.ActualHeight / 2), view),
                        layout.TranslatePoint(new Point(layout.ActualWidth - 2, layout.ActualHeight / 2), view),
                    };
                    foreach (var p in points)
                    {
                        var hit = VisualTreeHelper.HitTest(view, p)?.VisualHit as DependencyObject;
                        Assert.True(hit != null && Within(hit, layout), $"the tile does not take the hover at {p}");
                    }
                });
            });

        [Fact]
        public void SourceCard_Compact_Armed_PlacingSitsOnTheThumbnail_AndNotInTheChipRow() =>
            RunSta(() =>
            {
                var vm = NamedSource("TX2");
                vm.UsedInBlocks = 2;
                vm.IsArmed = true;
                TileAt(vm, true, view =>
                {
                    var thumb = (System.Windows.Controls.Border)Named(view, "CompactThumb").Single();
                    var placing = (System.Windows.Controls.Border)Named(view, "CompactPlacingBadge").Single();
                    Assert.True(placing.IsVisible);
                    Assert.True(Within(placing, thumb), "Placing is not inside the thumbnail");
                    Assert.DoesNotContain(FindAll<WrapPanel>(view), wrap => Within(placing, wrap));

                    var box = BoundsIn(placing, view);
                    var pic = BoundsIn(thumb, view);
                    Assert.True(box.Left >= pic.Left && box.Right <= pic.Right && box.Top >= pic.Top && box.Bottom <= pic.Bottom,
                        $"Placing {box} is not inside the thumbnail {pic}");

                    var text = FindAll<TextBlock>(placing).Single();
                    Assert.Equal("Placing", text.Text);
                    Assert.True(text.FontSize >= 12, $"Placing is {text.FontSize} px");

                    Assert.True(Named(view, "ArmedRing").Single().IsVisible);

                    var staged = (System.Windows.Controls.Border)Named(view, "CompactUsedInBadge").Single();
                    Assert.True(staged.IsVisible);
                    Assert.False(Within(staged, thumb), "the staged chip moved onto the thumbnail");
                    Assert.True(view.ActualHeight <= 110, $"the compact tile is {view.ActualHeight:F1} high");
                });

                var idle = NamedSource("TX2");
                idle.UsedInBlocks = 2;
                TileAt(idle, true, view => Assert.False(Named(view, "CompactPlacingBadge").Single().IsVisible));
            });

        [Fact]
        public void SourceCard_Compact_Armed_PlacingDoesNotCoverThePlaceholderCaption() =>
            RunSta(() =>
            {
                foreach (var (avas, caption) in new[] { (true, "Standby"), (false, "No preview") })
                {
                    var vm = UiV2Source(avas: avas);
                    vm.UsedInBlocks = 2;
                    vm.IsArmed = true;
                    TileAt(vm, true, view =>
                    {
                        var placing = (System.Windows.Controls.Border)Named(view, "CompactPlacingBadge").Single();
                        var text = VisibleTexts(view, caption).Single();
                        var chip = BoundsIn(placing, view);
                        var word = BoundsIn(text, view);
                        Assert.False(chip.IntersectsWith(word), $"'{caption}' ({word}) is under Placing ({chip})");
                    });
                }
            });
    }
}
