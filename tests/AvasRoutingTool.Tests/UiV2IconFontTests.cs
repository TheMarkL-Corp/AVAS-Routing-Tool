using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Xunit;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// D3: stock glyphs are <c>ui:SymbolIcon</c> (Fluent System Icons, a font). A missing font resource gives a blank icon and no exception,
    /// so a rendered icon must prove it has a glyph, a resolved FluentSystemIcons family that really contains that glyph, and a size.
    /// </summary>
    [Collection("WpfSta")]
    public class UiV2IconFontTests
    {
        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try { TestResources.Ensure(); body(); }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("Icon font failure: " + failure);
        }

        /// <summary>The icons the UI v2 plans name (app bar, Logs toolbar, cards, Composer).</summary>
        public static IEnumerable<object[]> PlannedSymbols() => new[]
        {
            SymbolRegular.Settings24, SymbolRegular.ArrowSync24, SymbolRegular.WeatherSunny24, SymbolRegular.WeatherMoon24,
            SymbolRegular.Broom24, SymbolRegular.FolderOpen24, SymbolRegular.Info24, SymbolRegular.Play24, SymbolRegular.Stop24,
            SymbolRegular.Checkmark24, SymbolRegular.ChevronDown24, SymbolRegular.LockClosed24, SymbolRegular.ArrowUndo24,
            SymbolRegular.Dismiss24, SymbolRegular.Delete24, SymbolRegular.Warning24,
        }.Select(s => new object[] { s });

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }

        private static SymbolIcon Render(SymbolRegular symbol, bool filled, double size, Action<SymbolIcon> assert)
        {
            var icon = new SymbolIcon { Symbol = symbol, Filled = filled, FontSize = size };
            var window = new Window { Content = icon, Width = 200, Height = 200, ShowInTaskbar = false, Left = -10000 };
            try
            {
                window.Show();
                window.UpdateLayout();
                assert(icon);
            }
            finally { window.Close(); }
            return icon;
        }

        [Theory]
        [MemberData(nameof(PlannedSymbols))]
        public void ARenderedSymbolIcon_HasAGlyph_InTheFluentSystemIconsFont_AndAnArea(SymbolRegular symbol) =>
            RunSta(() => Render(symbol, filled: false, size: 22, icon =>
            {
                Assert.False(string.IsNullOrEmpty(icon.Glyph), "no glyph");
                Assert.Contains("FluentSystemIcons", icon.FontFamily.Source ?? "");
                Assert.DoesNotContain("Filled", icon.FontFamily.Source ?? "");

                var typeface = new Typeface(icon.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                Assert.True(typeface.TryGetGlyphTypeface(out var glyphs), "the font did not resolve to a glyph typeface");
                Assert.True(glyphs.CharacterToGlyphMap.ContainsKey(icon.Glyph[0]), $"the font has no glyph for U+{(int)icon.Glyph[0]:X4}");

                var text = Descendants<TextBlock>(icon).Single();
                Assert.Equal(icon.Glyph, text.Text);
                Assert.True(icon.ActualWidth > 0 && icon.ActualHeight > 0, $"icon area {icon.ActualWidth}x{icon.ActualHeight}");
            }));

        [Fact]
        public void TheFilledVariant_UsesTheFilledFontFamily() =>
            RunSta(() => Render(SymbolRegular.Checkmark24, filled: true, size: 22, icon =>
            {
                Assert.Contains("Filled", icon.FontFamily.Source ?? "", StringComparison.OrdinalIgnoreCase);
                var typeface = new Typeface(icon.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                Assert.True(typeface.TryGetGlyphTypeface(out var glyphs));
                Assert.True(glyphs.CharacterToGlyphMap.ContainsKey(icon.Glyph[0]));
            }));

        [Theory]
        [InlineData("Size.Icon.S", 16.0)]
        [InlineData("Size.Icon.M", 22.0)]
        [InlineData("Size.Icon.L", 28.0)]
        public void AnIconSizedByTheIconTokens_RendersAtThatSize(string token, double size) =>
            RunSta(() => Render(SymbolRegular.Settings24, filled: false, (double)Application.Current.FindResource(token), icon =>
            {
                Assert.Equal(size, icon.FontSize);
                // A 24 px Fluent glyph scales with FontSize: the box is at least the font size wide and tall.
                Assert.True(icon.ActualHeight >= size - 1 && icon.ActualWidth >= size - 1, $"{icon.ActualWidth}x{icon.ActualHeight} at {size}");
            }));

        [Theory]
        [InlineData("FluentSystemIcons", "#FluentSystemIcons-Regular")]
        [InlineData("FluentSystemIconsFilled", "#FluentSystemIcons-Filled")]
        public void TheIconFontFamilies_AreApplicationResources_FromTheControlsDictionary(string key, string face) =>
            RunSta(() =>
            {
                var family = Assert.IsType<FontFamily>(Application.Current.FindResource(key));
                Assert.EndsWith(face, family.Source);
                Assert.Contains("Wpf.Ui", family.Source);
            });
    }
}
