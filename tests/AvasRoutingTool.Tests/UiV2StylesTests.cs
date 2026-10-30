using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AvasRoutingTool.Services;
using AvasRoutingTool.Views;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using ScrollViewer = System.Windows.Controls.ScrollViewer;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// UI v2 foundation (F4): the shared styles live in <c>Resources/Styles/*</c> under unchanged keys; button, text, badge and list
    /// styles behave as specified.
    /// </summary>
    [Collection("WpfSta")]
    public class UiV2StylesTests
    {
        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    InputModality.IsKeyboard = false;
                    TestResources.Ensure();
                    body();
                }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("Styles failure: " + failure);
        }

        private static string SourceRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AvasRoutingTool.sln"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "src", "AvasRoutingTool");
        }

        /// <summary>The file name of the merged dictionary that directly holds the key.</summary>
        private static string HomeOf(object key)
        {
            var home = Application.Current.Resources.MergedDictionaries.LastOrDefault(d => d.Contains(key)); // the last one wins a lookup
            Assert.True(home != null, $"{key} is in no merged dictionary");
            return Path.GetFileName(home!.Source!.OriginalString);
        }

        private static double Token(string key) => (double)Application.Current.FindResource(key);

        private static object Setter(Style style, DependencyProperty property)
        {
            for (var s = style; s != null; s = s.BasedOn)
            {
                var setter = s.Setters.OfType<Setter>().FirstOrDefault(x => x.Property == property);
                if (setter != null) return setter.Value;
            }
            throw new Xunit.Sdk.XunitException($"no setter for {property.Name}");
        }

        public static IEnumerable<object[]> KeysAndHomes() => new[]
        {
            ("Style.FocusVisual.Ring", "Buttons.xaml"),
            ("Style.Button.Primary", "Buttons.xaml"), ("Style.Button.Secondary", "Buttons.xaml"), ("Style.Button.Unroute", "Buttons.xaml"),
            ("Style.Button.Composer", "Buttons.xaml"), ("Style.Button.CardAction", "Buttons.xaml"), ("Style.Button.AppBar", "Buttons.xaml"),
            ("Style.Button.Chip.Staged", "Buttons.xaml"), ("Style.Icon.AppBar", "Buttons.xaml"),
            ("Style.Tab.Nav", "Tabs.xaml"),
            ("Style.Text.Lead", "Text.xaml"), ("Style.Text.Title", "Text.xaml"), ("Style.Text.Section", "Text.xaml"), ("Style.Text.Body", "Text.xaml"),
            ("Style.Text.Caption", "Text.xaml"), ("Style.Text.Mono", "Text.xaml"), ("Style.Text.ComposerLabel", "Text.xaml"),
            ("Style.Badge.Base", "Badges.xaml"), ("Style.Badge.Info", "Badges.xaml"), ("Style.Badge.Staged", "Badges.xaml"), ("Style.Badge.Armed", "Badges.xaml"),
            ("Style.ListBox.Virtualized", "Lists.xaml"),
            ("Converter.Visible.True", "Converters.xaml"), ("Converter.Visible.False", "Converters.xaml"), ("Converter.Visible.NotNull", "Converters.xaml"),
            ("Converter.Visible.Null", "Converters.xaml"), ("Converter.Visible.NotEmpty", "Converters.xaml"), ("Converter.Visible.HasItems", "Converters.xaml"),
            ("Converter.Visible.NoItems", "Converters.xaml"), ("Converter.WidthAtLeast", "Converters.xaml"), ("Converter.BoolToGridLength", "Converters.xaml"),
            // Take moved to Composer.xaml (X7: only the Composer uses it); the scroll bar family belongs with the implicit ScrollViewer.
            ("Style.Button.Take", "Composer.xaml"), ("Style.RepeatButton.ScrollPage", "Theme.xaml"), ("Style.Thumb.Scroll", "Theme.xaml"),
            ("Style.ScrollBar.Thin", "Theme.xaml"),
        }.Select(t => new object[] { t.Item1, t.Item2 });

        [Theory]
        [MemberData(nameof(KeysAndHomes))]
        public void Key_Resolves_AndLivesInItsCentralFile(string key, string file) =>
            RunSta(() =>
            {
                Assert.NotNull(Application.Current.FindResource(key));
                Assert.Equal(file, HomeOf(key));
            });

        [Fact]
        public void TheImplicitScrollViewerStyle_StillWinsOverWpfUi_AndKeepsItsGutters() =>
            RunSta(() =>
            {
                Assert.Equal("Theme.xaml", HomeOf(typeof(ScrollViewer)));
                var scroll = new ScrollViewer { Style = (Style)Application.Current.FindResource(typeof(ScrollViewer)) };
                scroll.ApplyTemplate();
                Assert.NotNull(scroll.Template.FindName("VerticalGutter", scroll));
                Assert.NotNull(scroll.Template.FindName("HorizontalGutter", scroll));
            });

        [Fact]
        public void TheSharedStyleFiles_AreMergedAfterThePalette_WithTheEmptySessionFilesLastBeforeThemeXaml() =>
            RunSta(() =>
            {
                var order = Application.Current.Resources.MergedDictionaries.Select(d => d.Source == null ? d.GetType().Name : Path.GetFileName(d.Source.OriginalString)).ToList();
                foreach (var file in new[] { "Buttons.xaml", "Tabs.xaml", "Text.xaml", "Badges.xaml", "Lists.xaml", "Cards.xaml", "Inputs.xaml", "Overlays.xaml", "SourceCard.xaml", "DisplayCard.xaml", "Composer.xaml" })
                {
                    Assert.True(order.IndexOf(file) > order.IndexOf("Theme.Dark.xaml"), file + " must come after the palette");
                    Assert.True(order.IndexOf(file) < order.IndexOf("Theme.xaml"), file + " must come before Theme.xaml");
                }
                Assert.True(order.IndexOf("Composer.xaml") > order.IndexOf("Buttons.xaml"), "Composer.xaml is merged after the shared files");
                Assert.Equal("Composer.xaml", order[order.IndexOf("Theme.xaml") - 1]);
            });

        [Fact]
        public void MainWindow_HasNoStylesOfItsOwn_TheyLiveInTheCentralFiles()
        {
            var xaml = File.ReadAllText(Path.Combine(SourceRoot(), "MainWindow.xaml"));
            foreach (var key in new[] { "Style.Tab.Nav", "Style.Button.AppBar", "Style.Icon.AppBar" })
            {
                Assert.DoesNotContain($"x:Key=\"{key}\"", xaml);
            }
            Assert.DoesNotContain("<Window.Resources>", xaml);
        }

        [Fact]
        public void ThemeXaml_HoldsOnlyTheScrollBarFamily()
        {
            var xaml = File.ReadAllText(Path.Combine(SourceRoot(), "Resources", "Theme.xaml"));
            var keys = System.Text.RegularExpressions.Regex.Matches(xaml, "<Style x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value).OrderBy(k => k).ToArray();
            Assert.Equal(new[] { "Style.RepeatButton.ScrollPage", "Style.ScrollBar.Thin", "Style.Thumb.Scroll" }, keys);
        }

        [Theory]
        [InlineData("Style.Button.Composer")]
        [InlineData("Style.Button.CardAction")]
        [InlineData("Style.Button.AppBar")]
        [InlineData("Style.Tab.Nav")]
        public void TouchButtons_AreAtLeast48_FromTheTouchToken(string key) =>
            RunSta(() =>
            {
                var style = (Style)Application.Current.FindResource(key);
                Assert.Equal(Token("Size.Touch.Min"), Setter(style, FrameworkElement.MinHeightProperty));
                Assert.Equal(48.0, Token("Size.Touch.Min"));
            });

        [Fact]
        public void ComposerButton_RendersAt48_NotAt56() =>
            RunSta(() =>
            {
                var button = new Button { Content = "Go", VerticalAlignment = VerticalAlignment.Top, Style = (Style)Application.Current.FindResource("Style.Button.Composer") };
                var window = new Window { Content = button, Width = 300, Height = 200, ShowInTaskbar = false, Left = -10000 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    Assert.Equal(48.0, button.ActualHeight);
                }
                finally { window.Close(); }
            });

        [Fact]
        public void ChipStaged_IsTheDraftChipTemplate_WithTheFocusRingTrigger() =>
            RunSta(() =>
            {
                var style = (Style)Application.Current.FindResource("Style.Button.Chip.Staged");
                var template = (ControlTemplate)Setter(style, Control.TemplateProperty);
                var triggers = template.Triggers.OfType<Trigger>().ToList();
                Assert.Contains(triggers, t => t.Property == InputModality.ShowsFocusProperty);
                Assert.Contains(triggers, t => t.Property == UIElement.IsMouseOverProperty);

                var chip = new Button { Style = style, Content = "DRAFT" };
                var window = new Window { Content = chip, Width = 300, Height = 200, ShowInTaskbar = false, Left = -10000 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var frame = (System.Windows.Shapes.Rectangle)chip.Template.FindName("DraftFrame", chip);
                    Assert.Equal(1.0, frame.StrokeThickness);
                    chip.SetValue(InputModality.ShowsFocusProperty, true);
                    Assert.Equal(2.0, frame.StrokeThickness);
                }
                finally { window.Close(); }
            });

        [Theory]
        [InlineData("Style.Text.Lead", 16.0)]
        [InlineData("Style.Text.Title", 14.0)]
        [InlineData("Style.Text.Section", 14.0)]
        [InlineData("Style.Text.Body", 13.0)]
        [InlineData("Style.Text.Caption", 12.0)]
        [InlineData("Style.Text.Mono", 12.0)]
        public void TextRoles_UseTheFontTokens(string key, double size) =>
            RunSta(() =>
            {
                var style = (Style)Application.Current.FindResource(key);
                Assert.Equal(size, (double)Setter(style, TextBlock.FontSizeProperty));
            });

        [Fact]
        public void SectionText_IsALevel2Heading_AndMonoIsConsolas() =>
            RunSta(() =>
            {
                var section = (Style)Application.Current.FindResource("Style.Text.Section");
                Assert.Equal(AutomationHeadingLevel.Level2, Setter(section, AutomationProperties.HeadingLevelProperty));
                var mono = (Style)Application.Current.FindResource("Style.Text.Mono");
                Assert.StartsWith("Consolas", ((FontFamily)Setter(mono, TextBlock.FontFamilyProperty)).Source);
            });

        [Fact]
        public void EveryButtonTextAndBadgeStyle_UsesNoFontSizeBelowTwelve() =>
            RunSta(() =>
            {
                var keys = Application.Current.Resources.MergedDictionaries
                    .Where(d => d.Source != null && d.Source.OriginalString.Contains("Styles/", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(d => d.Keys.OfType<string>().Select(k => (Dict: d, Key: k)))
                    .Where(x => x.Dict[x.Key] is Style)
                    .ToList();
                Assert.NotEmpty(keys);
                foreach (var (dict, key) in keys)
                {
                    for (var s = (Style)dict[key]; s != null; s = s.BasedOn)
                    {
                        foreach (var setter in s.Setters.OfType<Setter>().Where(x => x.Property.Name == "FontSize"))
                        {
                            // A style based on a WPF-UI control (ui:Card) inherits its FontSize as a theme resource: judge the size it resolves to.
                            var value = setter.Value is DynamicResourceExtension dynamic ? Application.Current.FindResource(dynamic.ResourceKey) : setter.Value;
                            Assert.True(value is double size && size >= 12, $"{key} sets FontSize {setter.Value}");
                        }
                    }
                }
            });

        // ----- badges -----

        private static double Luminance(Color c)
        {
            static double Channel(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        private static double Contrast(Color a, Color b)
        {
            var (hi, lo) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
            return (hi + 0.05) / (lo + 0.05);
        }

        [Theory]
        [InlineData("Style.Badge.Info", ThemeMode.Dark)]
        [InlineData("Style.Badge.Info", ThemeMode.Light)]
        [InlineData("Style.Badge.Staged", ThemeMode.Dark)]
        [InlineData("Style.Badge.Staged", ThemeMode.Light)]
        [InlineData("Style.Badge.Armed", ThemeMode.Dark)]
        [InlineData("Style.Badge.Armed", ThemeMode.Light)]
        public void Badges_AreWpfUiBadges_WithReadableText_InBothThemes(string key, ThemeMode mode) =>
            RunSta(() =>
            {
                var style = (Style)Application.Current.FindResource(key);
                Assert.Equal(typeof(Badge), style.TargetType);
                Assert.NotNull(style.BasedOn);

                new ThemeService().SetTheme(mode);
                var badge = new Badge { Content = "in 2 blocks", Style = style };
                var window = new Window { Content = badge, Width = 300, Height = 200, ShowInTaskbar = false, Left = -10000 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var text = ((SolidColorBrush)badge.Foreground).Color;
                    // The badge fills are translucent tints: they sit on the card.
                    var card = ((SolidColorBrush)Application.Current.FindResource("Brush.Surface.Card")).Color;
                    var tint = ((SolidColorBrush)badge.Background).Color;
                    var a = tint.A / 255.0;
                    var fill = Color.FromRgb((byte)(tint.R * a + card.R * (1 - a)), (byte)(tint.G * a + card.G * (1 - a)), (byte)(tint.B * a + card.B * (1 - a)));
                    Assert.True(Contrast(text, fill) >= 4.5, $"{key} in {mode}: {Contrast(text, fill):0.0}:1");
                    Assert.True(badge.FontSize >= 12);
                }
                finally { window.Close(); }
            });

        // ----- virtualized list -----

        [Fact]
        public void VirtualizedListBox_RealizesOnlyTheVisibleRows_RecyclesThem_AndUsesAPlainScrollViewer() =>
            RunSta(() =>
            {
                var list = new ListBox
                {
                    Style = (Style)Application.Current.FindResource("Style.ListBox.Virtualized"),
                    ItemsSource = Enumerable.Range(0, 5000).Select(i => "row " + i).ToList(),
                    Height = 300,
                    Width = 400,
                };
                var window = new Window { Content = list, Width = 500, Height = 400, ShowInTaskbar = false, Left = -10000 };
                try
                {
                    window.Show();
                    window.UpdateLayout();

                    var realized = Descendants<ListBoxItem>(list).Count();
                    Assert.InRange(realized, 1, 60);
                    Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
                    Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
                    Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(list));
                    Assert.IsType<VirtualizingStackPanel>(Descendants<Panel>(list).First(p => p.IsItemsHost));

                    var scroll = Descendants<ScrollViewer>(list).Single();
                    Assert.Equal(typeof(ScrollViewer), scroll.GetType()); // not WPF-UI's PassiveScrollViewer
                    Assert.NotNull(scroll.Template.FindName("VerticalGutter", scroll)); // the app's 16 px gutters apply
                    Assert.True(scroll.CanContentScroll);
                }
                finally { window.Close(); }
            });

        [Fact]
        public void VirtualizedListBox_Rows_CarryNoWpfUiItemChrome() =>
            RunSta(() =>
            {
                var list = new ListBox
                {
                    Style = (Style)Application.Current.FindResource("Style.ListBox.Virtualized"),
                    ItemsSource = new[] { "one", "two" },
                    Height = 100,
                    Width = 200,
                };
                var window = new Window { Content = list, Width = 300, Height = 200, ShowInTaskbar = false, Left = -10000 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var item = Descendants<ListBoxItem>(list).First();
                    Assert.Equal(new Thickness(0), item.Padding);
                    Assert.Equal(new Thickness(0), item.Margin);
                    Assert.Empty(Descendants<Border>(item)); // no WPF-UI hover/selection border around the row content
                }
                finally { window.Close(); }
            });

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }
    }
}
