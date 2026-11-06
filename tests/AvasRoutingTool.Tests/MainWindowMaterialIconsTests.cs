using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Controls;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;
using Path = System.IO.Path;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Round 3: the Main Window scope (Visual Routing tab and the shared app bar) draws every icon with MaterialIcon.
    /// Composer, Room Cameras, PTZ pad, Advanced, Settings and the dialogs keep their Fluent icons (UiV2IconFontTests covers those).
    /// </summary>
    [Collection("WpfSta")]
    public class MainWindowMaterialIconsTests
    {
        public sealed record ThemeContext(bool IsLightMode);

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
            if (failure != null) throw new Xunit.Sdk.XunitException("Material icons failure: " + failure);
        }

        private static void WithWindow(Action<MainWindow> body) => RunSta(() =>
        {
            var window = new MainWindow { WindowState = WindowState.Normal, Width = 1920, Height = 1040, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
            window.Show();
            window.UpdateLayout();
            try { body(window); } finally { window.Close(); }
        });

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is T t) yield return t;
                foreach (var d in Descendants<T>(c)) yield return d;
            }
        }

        private static string Src(string relative) => File.ReadAllText(Path.Combine(UiV2Allowlist.SourceRoot(), relative));

        /// <summary>Every file of the scope that can name a Kind: the views, their code-behind and the shared styles that set one.</summary>
        private static readonly string[] ScopeFiles =
        {
            "MainWindow.xaml", "MainWindow.xaml.cs",
            "Views/DisplayCardView.xaml", "Views/DisplayCardView.xaml.cs",
            "Views/SourceCardView.xaml", "Views/SourceCardView.xaml.cs",
            "Views/VisualRoutingView.xaml", "Views/VisualRoutingView.xaml.cs",
            "Resources/Styles/Buttons.xaml", "Resources/Styles/Cards.xaml", "Resources/Styles/DisplayCard.xaml",
        };

        private static readonly Regex KindInXaml = new("(?:Kind|Property=\"Kind\" Value)=\"([a-z][a-z_0-9]*)\"", RegexOptions.Compiled);
        private static readonly Regex KindInCode = new("Kind\\s*=\\s*\"([a-z][a-z_0-9]*)\"", RegexOptions.Compiled);

        private static bool IsInside<T>(DependencyObject d) where T : DependencyObject
        {
            for (var p = VisualTreeHelper.GetParent(d); p != null; p = VisualTreeHelper.GetParent(p)) if (p is T) return true;
            return false;
        }

        [Fact]
        public void MainWindow_VisualRouting_HasNoFluentSymbolIcons() => WithWindow(w =>
        {
            Assert.Empty(Descendants<SymbolIcon>(w));

            // Hand-drawn icon geometries (everything but the brand mark, Icon.Logo.*) are gone from the resources.
            foreach (var key in new[] { "Icon.Video", "Icon.Monitor", "Icon.Grid", "Icon.Copy", "Icon.Power" })
                Assert.Null(Application.Current.TryFindResource(key));
            Assert.All(Descendants<System.Windows.Shapes.Path>(w).Where(p => p.TemplatedParent == null && p.Data != null), p =>
                Assert.True(IsInside<BrandMark>(p) || IsInside<LayoutSchematic>(p), "a hand-drawn Path icon outside the brand mark and the schematics"));

            // The templates are not all in the tree (no devices here): the XAML of the scope is clean too.
            foreach (var file in ScopeFiles.Where(f => f.EndsWith(".xaml", StringComparison.Ordinal) && !f.Contains("Styles/")))
            {
                var text = Src(file);
                Assert.DoesNotContain("SymbolIcon", text);
                Assert.Empty(Regex.Matches(text, "StaticResource Icon\\.(?!Logo\\.)\\w+").Select(m => m.Value));
            }
        });

        [Fact]
        public void AppBar_Icons_AreTheMaterialSettingsSyncAndThemeKinds() => WithWindow(w =>
        {
            Assert.Equal("settings", Descendants<MaterialIcon>((Button)w.FindName("SettingsButton")).Single().Kind);
            Assert.Equal("sync", Descendants<MaterialIcon>((Button)w.FindName("RefreshButton")).Single().Kind);
            var theme = Descendants<MaterialIcon>((Button)w.FindName("ThemeToggleButton")).Single();
            Assert.Same(Application.Current.FindResource("Style.Icon.ThemeToggle"), theme.Style);
            var size = (double)Application.Current.FindResource("Size.Icon.M");
            foreach (var name in new[] { "SettingsButton", "RefreshButton", "ThemeToggleButton" })
            {
                var button = (Button)w.FindName(name);
                var icon = Descendants<MaterialIcon>(button).Single();
                Assert.Equal(size, icon.Width);
                Assert.Same(button.Foreground, icon.Foreground);
            }
        });

        [Fact]
        public void LogsToolbar_Icons_AreMaterialBroomAndFolder() => RunSta(() =>
        {
            var text = Src("MainWindow.xaml");
            Assert.Contains("Kind=\"cleaning_services\"", text);
            Assert.Contains("Kind=\"folder_open\"", text);
            Assert.DoesNotContain("Broom24", text);
            Assert.DoesNotContain("FolderOpen24", text);
        });

        [Fact]
        public void AppBar_ThemeToggle_SwapsSunAndMoonKinds() => RunSta(() =>
        {
            var icon = new MaterialIcon { Style = (Style)Application.Current.FindResource("Style.Icon.ThemeToggle") };
            var host = new Window { Content = icon, DataContext = new ThemeContext(false), Width = 200, Height = 200, ShowInTaskbar = false, Left = -10000 };
            try
            {
                host.Show();
                host.UpdateLayout();
                Assert.Equal("dark_mode", icon.Kind);
                host.DataContext = new ThemeContext(true);
                host.UpdateLayout();
                Assert.Equal("light_mode", icon.Kind);
                host.DataContext = new ThemeContext(false);
                host.UpdateLayout();
                Assert.Equal("dark_mode", icon.Kind);
            }
            finally { host.Close(); }
        });

        private static SourceCardViewModel Source() =>
            new(new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = "TX2", VendorId = 105, IsTransmitter = true, IsActive = true, LinkMode = "Single Link" });

        private static string KindIn(DependencyObject button) => Descendants<MaterialIcon>(button).Single().Kind;

        [Fact]
        public void SourceCard_StartStop_UsePlayAndStopKinds() => RunSta(() =>
        {
            var vm = Source();
            var view = new SourceCardView { DataContext = vm };
            var window = new Window { Content = view, Width = 400, Height = 400, ShowInTaskbar = false, Left = -10000 };
            try
            {
                window.Show();
                view.UpdateLayout();
                Assert.Empty(Descendants<SymbolIcon>(view));
                Assert.Equal("play_arrow", KindIn((Button)view.FindName("StartStreamButton")));
                vm.IsStreaming = true;
                view.UpdateLayout();
                Assert.Equal("stop", KindIn((Button)view.FindName("StopStreamButton")));
                Assert.Equal("sync", KindIn((Button)view.FindName("RefreshStreamButton")));
                var actionSize = (double)Application.Current.FindResource("Size.Icon.M");
                Assert.All(new[] { "StartStreamButton", "StopStreamButton", "RefreshStreamButton" },
                    n => Assert.Equal(actionSize, Descendants<MaterialIcon>((Button)view.FindName(n)).Single().Width));
                var small = (double)Application.Current.FindResource("Size.Icon.S");
                var header = Descendants<MaterialIcon>(view).Where(i => i.Kind is "edit" or "info").ToList();
                Assert.Equal(2, header.Count);
                Assert.All(header, i => Assert.Equal(small, i.Width));
            }
            finally { window.Close(); }
        });

        [Fact]
        public void Arrows_AreMaterialChevrons() => WithWindow(w =>
        {
            var view = (VisualRoutingView)w.FindName("ViewVisualRouting");
            var left = (Button)view.FindName("DisplaysScrollLeft");
            var right = (Button)view.FindName("DisplaysScrollRight");
            var l = Assert.IsType<MaterialIcon>(left.Content);
            var r = Assert.IsType<MaterialIcon>(right.Content);
            Assert.Equal("chevron_left", l.Kind);
            Assert.Equal("chevron_right", r.Kind);
            var size = (double)Application.Current.FindResource("Size.Icon.M");
            Assert.Equal(size, l.Width);
            Assert.Equal(size, r.Width);
            Assert.Equal("Scroll displays left", System.Windows.Automation.AutomationProperties.GetName(left));
            Assert.Equal("Scroll displays right", System.Windows.Automation.AutomationProperties.GetName(right));
        });

        [Fact]
        public void SectionHeaders_AndBanner_UseMaterialIcons() => WithWindow(w =>
        {
            var view = (VisualRoutingView)w.FindName("ViewVisualRouting");
            var section = (Style)Application.Current.FindResource("Style.Icon.Section");
            var icons = Descendants<MaterialIcon>(view).Where(i => i.Style == section).ToList();
            Assert.Equal(new[] { "videocam", "desktop_windows" }, icons.Select(i => i.Kind).ToArray());
            var small = (double)Application.Current.FindResource("Size.Icon.S");
            Assert.All(icons, i => Assert.Equal(small, i.Width));

            var text = Src("Views/VisualRoutingView.xaml");
            Assert.Contains("Kind=\"warning\"", text);
            Assert.Contains("Kind=\"close\"", text);
        });

        [Fact]
        public void EveryKind_UsedInTheMainWindowScope_HasAMaterialGeometry() => RunSta(() =>
        {
            var kinds = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var file in ScopeFiles)
            {
                var text = Src(file);
                var regex = file.EndsWith(".cs", StringComparison.Ordinal) ? KindInCode : KindInXaml;
                foreach (Match m in regex.Matches(text)) kinds.Add(m.Groups[1].Value);
            }
            Assert.NotEmpty(kinds);
            Assert.Contains("edit", kinds);
            Assert.Contains("play_arrow", kinds);
            Assert.Contains("chevron_left", kinds);
            Assert.Contains("light_mode", kinds);
            foreach (var kind in kinds)
                Assert.True(Application.Current.TryFindResource("MIcon." + kind) is Geometry, $"MIcon.{kind} is missing from Resources/MaterialIcons.xaml");
        });
    }
}
