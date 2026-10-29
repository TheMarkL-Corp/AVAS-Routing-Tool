using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using AvasRoutingTool.Controls;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// UI v2 foundation: sizes and font sizes come from named tokens (<c>Resources/Tokens.xaml</c>), never from literals in views.
    /// D4: exactly two touch tokens (48 / 36). D8: 16/14/13/12 and nothing below 12.
    /// </summary>
    [Collection("WpfSta")]
    public class UiV2TokenTests
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
            if (failure != null) throw new Xunit.Sdk.XunitException("Token failure: " + failure);
        }

        public static IEnumerable<object[]> DoubleTokens() => new[]
        {
            ("Size.Touch.Min", 48.0), ("Size.Touch.Compact", 36.0),
            ("Size.Icon.S", 16.0), ("Size.Icon.M", 22.0), ("Size.Icon.L", 28.0), ("Size.Icon.Brand", 28.0),
            ("Size.Dot.S", 8.0), ("Size.Dot.M", 10.0),
            ("Size.Card.Min", 320.0), ("Size.Card.Row.Header", 24.0), ("Size.Card.Row.ActionBar", 56.0), ("Size.Card.Row.Dock", 64.0),
            ("Size.AppBar.Height", 56.0), ("Size.Tab.MinWidth", 120.0), ("Size.Breakpoint.AppBarLabels", 1400.0),
            ("Size.Window.Default.Width", 1280.0), ("Size.Window.Default.Height", 860.0),
            ("Size.Window.Min.Width", 1024.0), ("Size.Window.Min.Height", 720.0),
            ("Size.Dock.Content", 56.0), ("Size.Toast.Min", 56.0), ("Size.Notice.Max", 320.0),
            ("Size.Popup.LayoutMenu", 336.0), ("Size.LayoutTile.Min", 88.0),
            ("Size.Schematic.Menu.Width", 72.0), ("Size.Schematic.Menu.Height", 40.0),
            ("Size.Dialog.Settings.Width", 480.0), ("Size.Button.Dialog.Min", 110.0),
            ("Font.Size.Lead", 16.0), ("Font.Size.Title", 14.0), ("Font.Size.Body", 13.0), ("Font.Size.Caption", 12.0),
        }.Select(t => new object[] { t.Item1, t.Item2 });

        [Theory]
        [MemberData(nameof(DoubleTokens))]
        public void Token_HasItsValue_AsADouble(string key, double expected) =>
            RunSta(() => Assert.Equal(expected, (double)Application.Current.FindResource(key)));

        [Theory]
        [InlineData("Size.Log.Col.Time", 170)]
        [InlineData("Size.Log.Col.Level", 70)]
        [InlineData("Size.Log.Col.Category", 120)]
        public void LogColumnTokens_AreGridLengths(string key, double pixels) =>
            RunSta(() => Assert.Equal(new GridLength(pixels), (GridLength)Application.Current.FindResource(key)));

        [Fact]
        public void TouchTokens_AreExactlyMinAndCompact_NoDraftSizesLeft() =>
            RunSta(() =>
            {
                var tokens = TokenKeys();
                Assert.Equal(new[] { "Size.Touch.Compact", "Size.Touch.Min" }, tokens.Where(k => k.StartsWith("Size.Touch.")).OrderBy(k => k).ToArray());
            });

        [Fact]
        public void FontTokens_AreTheFourRoles_AndNoneIsBelowTwelve() =>
            RunSta(() =>
            {
                var fonts = TokenKeys().Where(k => k.StartsWith("Font.Size.")).OrderBy(k => k).ToArray();
                Assert.Equal(new[] { "Font.Size.Body", "Font.Size.Caption", "Font.Size.Lead", "Font.Size.Title" }, fonts);
                Assert.All(fonts, k => Assert.True((double)Application.Current.FindResource(k) >= 12, k + " is below the 12 px floor"));
            });

        [Fact]
        public void CardTokens_AgreeWithTheAdaptiveGridSpec() =>
            RunSta(() =>
            {
                double Token(string key) => (double)Application.Current.FindResource(key);
                Assert.Equal(GridSpec.Default.MinCardWidth, Token("Size.Card.Min"));
                // The display card: frame 2 + padding 24 + the three fixed rows around the 16:9 picture.
                var rows = Token("Size.Card.Row.Header") + Token("Size.Card.Row.ActionBar") + Token("Size.Card.Row.Dock");
                Assert.Equal(GridSpec.DisplayCard.ChromeHeight, 26 + rows);
            });

        /// <summary>Every token key of Tokens.xaml (the file holds nothing else).</summary>
        private static string[] TokenKeys()
        {
            var tokens = Application.Current.Resources.MergedDictionaries
                .Single(d => d.Source?.OriginalString.EndsWith("Resources/Tokens.xaml", StringComparison.OrdinalIgnoreCase) == true);
            var keys = tokens.Keys.Cast<object>().Select(k => k.ToString()!).ToArray();
            Assert.All(keys, k => Assert.True(k.StartsWith("Size.") || k.StartsWith("Font."), $"Tokens.xaml holds a non-token key: {k}"));
            return keys;
        }
    }
}
