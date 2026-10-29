using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Xml.Linq;
using AvasRoutingTool.Services;
using Wpf.Ui.Markup;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>The shared test bootstrap must give a test Application the same resources the real App.xaml does.</summary>
    [Collection("WpfSta")]
    public class TestResourcesTests
    {
        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("Test resources failure: " + failure);
        }

        private static string Describe(ResourceDictionary d) =>
            d switch
            {
                ThemesDictionary => "ThemesDictionary",
                ControlsDictionary => "ControlsDictionary",
                _ => NormalizeSource(d.Source?.OriginalString ?? "?")
            };

        private static string NormalizeSource(string source)
        {
            const string marker = "component/";
            var i = source.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? source[(i + marker.Length)..] : source;
        }

        private static string[] AppXamlMerges()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AvasRoutingTool.sln"))) dir = dir.Parent;
            Assert.NotNull(dir);
            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace ui = "http://schemas.lepo.co/wpfui/2022/xaml";
            var path = Path.Combine(dir!.FullName, "src", "AvasRoutingTool", "App.xaml");
            return XDocument.Load(path).Descendants(wpf + "ResourceDictionary.MergedDictionaries").Single().Elements()
                .Select(e => e.Name == ui + "ThemesDictionary" ? "ThemesDictionary"
                    : e.Name == ui + "ControlsDictionary" ? "ControlsDictionary"
                    : (string)e.Attribute("Source")!)
                .ToArray();
        }

        [Fact]
        public void Ensure_MergesExactlyWhatAppXamlMerges_InTheSameOrder() =>
            RunSta(() =>
            {
                var app = TestResources.Ensure();

                var merged = app.Resources.MergedDictionaries.Select(Describe).ToArray();

                Assert.Equal(AppXamlMerges(), merged);
            });

        [Fact]
        public void Ensure_IsIdempotent_NoDuplicatesAfterManyCalls() =>
            RunSta(() =>
            {
                var app = TestResources.Ensure();
                var first = app.Resources.MergedDictionaries.Count;

                TestResources.Ensure();
                TestResources.Ensure();

                Assert.Equal(first, app.Resources.MergedDictionaries.Count);
            });

        [Fact]
        public void Ensure_StartsEveryTestInDark_AfterAnEarlierTestSwitchedTheTheme() =>
            RunSta(() =>
            {
                TestResources.Ensure();
                new ThemeService().SetTheme(ThemeMode.Light);

                var app = TestResources.Ensure();

                var background = ((System.Windows.Media.SolidColorBrush)app.FindResource("Brush.Background.Dark")).Color;
                Assert.True((0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0 < 0.25, "the palette is still Light");
                Assert.EndsWith("/Dark.xaml", app.Resources.MergedDictionaries.OfType<ThemesDictionary>().Single().Source!.OriginalString, StringComparison.OrdinalIgnoreCase);
            });

        [Fact]
        public void Ensure_ResolvesAppKeysWpfUiKeysAndIcons() =>
            RunSta(() =>
            {
                var app = TestResources.Ensure();

                Assert.NotNull(app.FindResource("Brush.Background.Dark"));
                Assert.NotNull(app.FindResource("Icon.Power"));
                Assert.NotNull(app.FindResource("TextFillColorPrimaryBrush"));
                Assert.NotNull(app.FindResource("Style.Button.Primary"));
            });
    }
}
