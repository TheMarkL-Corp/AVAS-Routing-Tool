using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Views;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>UI v2 app bar and brand (plan task A5): labels at Full HD, vector icons, the brand mark view, tokens instead of literals.</summary>
    [Collection("WpfSta")]
    public class UiV2AppBarTests
    {
        private static void RunSta(Action<MainWindow> body, double width = 1280, double height = 860)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    TestResources.Ensure();
                    var window = new MainWindow { WindowState = WindowState.Normal, Width = width, Height = height, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
                    window.Show();
                    window.UpdateLayout();
                    try { body(window); } finally { window.Close(); }
                }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("App bar failure: " + failure);
        }

        public sealed record ThemeContext(bool IsLightMode);

        private static readonly string[] LabelNames = { "ThemeToggleLabel", "RefreshLabel", "SettingsLabel" };

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is T t) yield return t;
                foreach (var d in Descendants<T>(c)) yield return d;
            }
        }

        [Fact]
        public void AppBar_ShowsLabelsAtFullHd() => RunSta(w =>
        {
            foreach (var name in LabelNames)
            {
                var label = (TextBlock)w.FindName(name);
                Assert.NotNull(label);
                Assert.True(label.IsVisible, $"{name} is hidden at 1920 wide");
                Assert.True(label.FontSize >= 12, $"{name} is {label.FontSize}px");
                Assert.False(string.IsNullOrWhiteSpace(label.Text));
                Assert.DoesNotContain(label.Text, ch => char.IsSurrogate(ch) || ch >= '←');
            }
            var theme = ((TextBlock)w.FindName("ThemeToggleLabel")).Text;
            Assert.True(theme is "Light" or "Dark", $"theme label is '{theme}'");
        }, 1920, 1040);

        [Fact]
        public void AppBar_HidesTheLabelsAtTheFloor_AndStillFits() => RunSta(w =>
        {
            foreach (var name in LabelNames)
                Assert.False(((TextBlock)w.FindName(name)).IsVisible, $"{name} is visible at 1024 wide");

            var buttons = new[] { "ThemeToggleButton", "RefreshButton", "SettingsButton" }.Select(n => (Button)w.FindName(n)).ToArray();
            Assert.True(buttons.Max(b => b.TranslatePoint(new Point(b.ActualWidth, 0), w).X) <= w.ActualWidth, "actions are clipped");
            Assert.All(buttons, b => Assert.True(b.ActualWidth >= 48 && b.ActualHeight >= 48));
        }, 1024, 720);

        [Fact]
        public void ActionIcons_AreSymbolIcons_WithAGlyph_AndTheButtonColour() => RunSta(w =>
        {
            foreach (var name in new[] { "ThemeToggleButton", "RefreshButton", "SettingsButton" })
            {
                var button = (Button)w.FindName(name);
                var icon = Descendants<SymbolIcon>(button).Single();
                Assert.False(string.IsNullOrEmpty(icon.Glyph), $"{name}: no glyph");
                Assert.Contains("FluentSystemIcons", icon.FontFamily.Source ?? "");
                Assert.True(icon.ActualWidth >= 16 && icon.ActualHeight >= 16);
                Assert.Same(button.Foreground, icon.Foreground);
            }
        });

        [Fact]
        public void TheThemeIcon_NamesTheActiveTheme_SunInLightMoonInDark() => RunSta(w =>
        {
            var style = (Style)Application.Current.FindResource("Style.Icon.ThemeToggle");
            var icon = new SymbolIcon { Style = style };
            var host = new Window { Content = icon, DataContext = new ThemeContext(false), Width = 200, Height = 200, ShowInTaskbar = false, Left = -10000 };
            try
            {
                host.Show();
                host.UpdateLayout();
                Assert.Equal(SymbolRegular.WeatherMoon24, icon.Symbol);
                host.DataContext = new ThemeContext(true);
                host.UpdateLayout();
                Assert.Equal(SymbolRegular.WeatherSunny24, icon.Symbol);
            }
            finally { host.Close(); }
        });

        [Fact]
        public void TheBrandMark_IsItsOwnView_InsideTheBrandLogoHost() => RunSta(w =>
        {
            var logo = (FrameworkElement)w.FindName("BrandLogo");
            Assert.True(logo.ActualWidth >= 28 && logo.ActualHeight >= 28);
            Assert.Equal((double)Application.Current.FindResource("Size.Icon.Brand"), logo.ActualWidth);
            var mark = Descendants<BrandMark>(logo).Single();
            Assert.True(mark.ActualWidth > 0 && mark.ActualHeight > 0);
        });

        [Fact]
        public void TheWindow_TakesItsSizesFromTokens() => RunSta(w =>
        {
            Assert.Equal((double)Application.Current.FindResource("Size.Window.Min.Width"), w.MinWidth);
            Assert.Equal((double)Application.Current.FindResource("Size.Window.Min.Height"), w.MinHeight);
            Assert.Equal(1024, w.MinWidth);
            Assert.Equal(720, w.MinHeight);
        });

        [Fact]
        public void TheBrandNameIsALeadRole_AndTheVersionACaption() => RunSta(w =>
        {
            var name = Descendants<TextBlock>((FrameworkElement)w.FindName("BrandBlock")).First(t => t.Text == "AVAS ROUTING TOOL");
            Assert.Equal((double)Application.Current.FindResource("Font.Size.Lead"), name.FontSize);
            var version = (TextBlock)w.FindName("AppVersionText");
            Assert.Equal((double)Application.Current.FindResource("Font.Size.Caption"), version.FontSize);
        });

        [Fact]
        public void TheConnectionDot_IsAStatusDot_FromTheCentralStyle() => RunSta(w =>
        {
            var badge = (FrameworkElement)w.FindName("ConnectionBadge");
            var dot = Descendants<System.Windows.Shapes.Ellipse>(badge).Single();
            Assert.Equal((double)Application.Current.FindResource("Size.Dot.M"), dot.ActualWidth);
            Assert.Equal(dot.ActualWidth, dot.ActualHeight);
            Assert.Equal((double)Application.Current.FindResource("Size.Touch.Min"), badge.MinHeight);
        });
    }
}
