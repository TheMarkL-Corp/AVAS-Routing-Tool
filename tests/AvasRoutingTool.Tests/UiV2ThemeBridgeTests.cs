using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using AvasRoutingTool.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// UI v2 foundation (D1): WPF-UI supplies the theme and the control styles only. The app's own palette stays the single
    /// source of truth, so <see cref="ThemeService"/> drives WPF-UI's <see cref="ThemesDictionary"/> and accent from it.
    /// </summary>
    [Collection("WpfSta")]
    public class UiV2ThemeBridgeTests
    {
        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    TestResources.Ensure();
                    body();
                }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("Theme bridge failure: " + failure);
        }

        private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

        private static Color ColorOf(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;

        private static string AppXamlPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AvasRoutingTool.sln"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "src", "AvasRoutingTool", "App.xaml");
        }

        [Fact]
        public void AppXaml_MergesWpfUiDictionariesFirst_ThenThePalette_ThenTheSharedStyles_ThenIcons()
        {
            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace ui = "http://schemas.lepo.co/wpfui/2022/xaml";
            var merged = XDocument.Load(AppXamlPath()).Descendants(wpf + "ResourceDictionary.MergedDictionaries").Single().Elements().ToList();

            string Tag(XElement e) => e.Name == ui + "ThemesDictionary" ? "ThemesDictionary"
                : e.Name == ui + "ControlsDictionary" ? "ControlsDictionary"
                : (string?)e.Attribute("Source") ?? e.Name.LocalName;
            var order = merged.Select(Tag).ToList();

            Assert.Equal("ThemesDictionary", order[0]);
            Assert.Equal("ControlsDictionary", order[1]);
            Assert.Equal("Resources/Themes/Theme.Dark.xaml", order[2]);
            Assert.Equal("Resources/Tokens.xaml", order[3]);
            Assert.True(order.IndexOf("Resources/Styles/WpfUiOverrides.xaml") > 3, "the shared styles come after the palette and the tokens");
            Assert.True(order.IndexOf("Resources/Theme.xaml") > order.IndexOf("Resources/Styles/WpfUiOverrides.xaml"), "Theme.xaml must come after the shared styles");
            Assert.Equal("Resources/Icons.xaml", order[^1]);
            Assert.Equal("Dark", (string?)merged[0].Attribute("Theme"));
        }

        /// <summary>
        /// WPF-UI's <c>ControlsDictionary</c> ships an implicit <c>Window</c> style (own template, <c>OverridesDefaultStyle</c>, 1100x600 and
        /// <c>AllowsTransparency</c>) that makes <c>new Window().Show()</c> throw and re-chromes every plain window (D1: normal Windows chrome).
        /// </summary>
        [Fact]
        public void WpfUiControlsDictionary_DoesNotRestyleAPlainWindow() =>
            RunSta(() =>
            {
                var style = (Style)Application.Current.FindResource(typeof(Window));
                Assert.Empty(style.Setters);
                Assert.Null(style.BasedOn);

                var window = new Window { ShowInTaskbar = false, Left = -10000, Width = 200, Height = 100 };
                try
                {
                    window.Show();
                    Assert.Equal(200, window.Width);
                }
                finally { window.Close(); }
            });

        [Theory]
        [InlineData(ThemeMode.Light, "Light")]
        [InlineData(ThemeMode.Dark, "Dark")]
        public void ThemeService_SwapsTheWpfUiThemesDictionary_WithTheAppTheme(ThemeMode mode, string file) =>
            RunSta(() =>
            {
                var service = new ThemeService();
                service.SetTheme(mode == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);

                service.SetTheme(mode);

                var themes = Application.Current.Resources.MergedDictionaries.OfType<ThemesDictionary>().ToList();
                Assert.NotEmpty(themes);
                Assert.All(themes, d => Assert.EndsWith($"/{file}.xaml", d.Source!.OriginalString, StringComparison.OrdinalIgnoreCase));
                // The WPF-UI text colour follows: dark text on Light, light text on Dark.
                var wpfUiText = ColorOf("TextFillColorPrimaryBrush");
                Assert.Equal(mode == ThemeMode.Light, Luminance(wpfUiText) < 0.5);
                // The app palette still decides the chrome.
                Assert.Equal(mode == ThemeMode.Light, Luminance(ColorOf("Brush.Background.Dark")) > 0.85);
            });

        [Fact]
        public void ThemeService_SetsTheWpfUiAccentFromTheAppAccentToken() =>
            RunSta(() =>
            {
                var service = new ThemeService();

                service.SetTheme(ThemeMode.Light);
                service.SetTheme(ThemeMode.Dark);

                var appAccent = ((SolidColorBrush)Application.Current.FindResource("Brush.Accent.Primary")).Color;
                var wpfUiAccent = (Color)Application.Current.FindResource("SystemAccentColor");
                Assert.Equal(appAccent, wpfUiAccent);
            });

        /// <summary>
        /// <c>ApplicationThemeManager.Apply</c> restyles <c>Application.MainWindow</c> as a WPF-UI window, which strips the native
        /// caption text, icon and system menu from our plain <c>Window</c> (D1). It raises <c>Changed</c>, so any call shows here.
        /// (The test Application is owned by another thread, so a call that touches <c>MainWindow</c> would also throw.)
        /// </summary>
        [Fact]
        public void ThemeService_NeverCallsApplicationThemeManagerApply() =>
            RunSta(() =>
            {
                var raised = 0;
                void OnChanged(ApplicationTheme theme, Color accent) => raised++;
                ApplicationThemeManager.Changed += OnChanged;
                try
                {
                    var service = new ThemeService();

                    service.SetTheme(ThemeMode.Light);
                    service.SetTheme(ThemeMode.Dark);
                    service.ToggleTheme();
                    service.ToggleTheme();

                    Assert.Equal(0, raised);
                }
                finally { ApplicationThemeManager.Changed -= OnChanged; }
            });
    }
}
