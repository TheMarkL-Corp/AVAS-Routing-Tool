using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AvasRoutingTool.Controls;
using Xunit;
using IoPath = System.IO.Path;
using ShapePath = System.Windows.Shapes.Path;

namespace AvasRoutingTool.Tests
{
    /// <summary>Material Symbols Rounded geometry set (Resources/MaterialIcons.xaml) and the MaterialIcon control.</summary>
    [Collection("WpfSta")]
    public class MaterialIconTests
    {
        /// <summary>The icon map of the plan; keep in step with the name list at the top of tools/icons/import-material-icons.ps1.</summary>
        public static readonly string[] MapNames =
        {
            "settings", "sync", "light_mode", "dark_mode", "cleaning_services", "folder_open", "edit", "info", "play_arrow", "stop", "check",
            "lock", "expand_more", "content_copy", "grid_view", "power_settings_new", "desktop_windows", "videocam", "chevron_left",
            "chevron_right", "close", "warning",
        };

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
            if (failure != null) throw new Xunit.Sdk.XunitException("MaterialIcon failure: " + failure);
        }

        private static ShapePath PathOf(MaterialIcon icon)
        {
            icon.ApplyTemplate();
            return (ShapePath)icon.Template.FindName(MaterialIcon.PathPartName, icon);
        }

        private static Window Host(FrameworkElement content)
        {
            var window = new Window { Content = content, Width = 300, Height = 300, ShowInTaskbar = false, Left = -10000 };
            window.Show();
            window.UpdateLayout();
            return window;
        }

        [Fact]
        public void EveryMapIcon_HasAGeometryInA24Box() => RunSta(() =>
        {
            var app = Application.Current;
            foreach (var name in MapNames)
            {
                var geometry = Assert.IsAssignableFrom<Geometry>(app.TryFindResource("MIcon." + name) ?? throw new Xunit.Sdk.XunitException("Missing MIcon." + name));
                var b = geometry.Bounds;
                Assert.False(b.IsEmpty || b.Width <= 0 || b.Height <= 0, $"MIcon.{name} is empty");
                Assert.True(b.Left >= -1 && b.Top >= -1 && b.Right <= 25 && b.Bottom <= 25, $"MIcon.{name} bounds {b} leave the 24 box");
                Assert.True(b.Width > 6 && b.Height > 6, $"MIcon.{name} bounds {b} look unscaled");
                var stream = Assert.IsType<StreamGeometry>(geometry);
                Assert.Equal(FillRule.Nonzero, stream.FillRule);
            }
        });

        [Fact]
        public void MaterialIcon_UnknownKind_ThrowsWithTheKindName() => RunSta(() =>
        {
            var icon = new MaterialIcon { Kind = "not_an_icon" };
            var ex = Assert.Throws<InvalidOperationException>(() => icon.ApplyTemplate());
            Assert.Contains("not_an_icon", ex.Message);
        });

        [Fact]
        public void MaterialIcon_FollowsForeground_InBothThemes() => RunSta(() =>
        {
            var app = Application.Current;
            var darkPalette = app.Resources.MergedDictionaries[2];
            try
            {
                foreach (var theme in new[] { "Dark", "Light" })
                {
                    app.Resources.MergedDictionaries[2] = theme == "Dark"
                        ? darkPalette
                        : new ResourceDictionary { Source = new Uri("pack://application:,,,/AvasRoutingTool;component/Resources/Themes/Theme.Light.xaml") };
                    var icon = new MaterialIcon { Kind = "settings" };
                    icon.SetResourceReference(Control.ForegroundProperty, "Brush.Text.Primary");
                    var window = Host(icon);
                    try
                    {
                        var expected = ((SolidColorBrush)app.FindResource("Brush.Text.Primary")).Color;
                        var fill = Assert.IsType<SolidColorBrush>(PathOf(icon).Fill);
                        Assert.Equal(expected, fill.Color);
                    }
                    finally { window.Close(); }
                }
            }
            finally { app.Resources.MergedDictionaries[2] = darkPalette; }

            // Inherited from a parent too (a button's text colour), and not baked into the geometry.
            var inherited = new MaterialIcon { Kind = "check" };
            var holder = new ContentControl { Content = inherited, Foreground = Brushes.Red };
            var w = Host(holder);
            try { Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(PathOf(inherited).Fill).Color); }
            finally { w.Close(); }
        });

        [Fact]
        public void MaterialIcon_SizeComesFromWidthAndHeight() => RunSta(() =>
        {
            var defaultIcon = new MaterialIcon { Kind = "edit" };
            var panel1 = new StackPanel(); panel1.Children.Add(defaultIcon);
            var w1 = Host(panel1);
            try
            {
                var m = (double)Application.Current.FindResource("Size.Icon.M");
                Assert.Equal(m, defaultIcon.ActualWidth);
                Assert.Equal(m, defaultIcon.ActualHeight);
            }
            finally { w1.Close(); }

            var small = new MaterialIcon { Kind = "edit", Width = 16, Height = 16 };
            var panel = new StackPanel(); panel.Children.Add(small);
            var w2 = Host(panel);
            try
            {
                Assert.Equal(16, small.ActualWidth);
                Assert.Equal(16, small.ActualHeight);
                var bounds = VisualTreeHelper.GetDescendantBounds(small);
                Assert.True(bounds.Width <= 16.01 && bounds.Height <= 16.01, $"drawn bounds {bounds} exceed 16 x 16");
            }
            finally { w2.Close(); }
        });

        [Fact]
        public void MaterialIcon_IsDecorative_UntilNamed() => RunSta(() =>
        {
            var icon = new MaterialIcon { Kind = "info" };
            Assert.False(icon.IsHitTestVisible);
            Assert.False(icon.Focusable);
            var peer = UIElementAutomationPeer.CreatePeerForElement(icon);
            Assert.False(peer.IsControlElement());
            Assert.False(peer.IsContentElement());
            System.Windows.Automation.AutomationProperties.SetName(icon, "Info");
            Assert.True(peer.IsControlElement());
        });

        /// <summary>Holes must stay holes: a wrong fill rule or arc would fill them. Renders and samples the alpha channel.</summary>
        [Fact]
        public void RenderedIcons_KeepTheirHoles() => RunSta(() =>
        {
            foreach (var (kind, hole, solid) in new[]
            {
                ("settings", new Point(12, 8), new Point(12, 3)),       // gear: the empty ring between the centre disc and the gear outline vs the outline
                ("info", new Point(7, 12), new Point(3, 12)),           // ring interior vs the ring itself
            })
            {
                var icon = new MaterialIcon { Kind = kind, Width = 240, Height = 240, Foreground = Brushes.Black };
                var host = new Canvas { Width = 240, Height = 240 };
                host.Children.Add(icon);
                var window = Host(host);
                try
                {
                    host.UpdateLayout();
                    var bmp = new RenderTargetBitmap(240, 240, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(icon);
                    byte AlphaAt(Point p)
                    {
                        var px = new byte[4];
                        bmp.CopyPixels(new Int32Rect((int)(p.X * 10), (int)(p.Y * 10), 1, 1), px, 4, 0);
                        return px[3];
                    }
                    Assert.True(AlphaAt(hole) == 0, $"{kind}: the hole at {hole} is filled (alpha {AlphaAt(hole)})");
                    Assert.True(AlphaAt(solid) > 200, $"{kind}: the body at {solid} is empty (alpha {AlphaAt(solid)})");
                }
                finally { window.Close(); }
            }
        });

        /// <summary>On demand: set AVAS_UIV2_SHOTS to a folder; renders all icons (48 px, dark and light, named) to material-icons-sheet.png. No-op without it.</summary>
        [Fact]
        public void ContactSheet_AllIcons()
        {
            var folder = Environment.GetEnvironmentVariable("AVAS_UIV2_SHOTS");
            if (string.IsNullOrWhiteSpace(folder)) return;
            System.IO.Directory.CreateDirectory(folder);
            RunSta(() =>
            {
                const int cols = 6;
                var grid = new Grid { Background = Brushes.Gray };
                var rows = (MapNames.Length + cols - 1) / cols;
                for (var c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                for (var r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(110) });
                for (var i = 0; i < MapNames.Length; i++)
                {
                    var name = MapNames[i];
                    Border Half(Brush bg, Brush fg, bool label)
                    {
                        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                        stack.Children.Add(new MaterialIcon { Kind = name, Width = 48, Height = 48, Foreground = fg });
                        if (label) stack.Children.Add(new TextBlock { Text = name, FontSize = 12, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center });
                        else stack.Children.Add(new TextBlock { Text = " ", FontSize = 12 });
                        return new Border { Background = bg, Child = stack };
                    }
                    var tile = new Grid { Margin = new Thickness(2) };
                    tile.ColumnDefinitions.Add(new ColumnDefinition()); tile.ColumnDefinitions.Add(new ColumnDefinition());
                    var dark = Half(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24)), Brushes.White, true);
                    var light = Half(Brushes.White, Brushes.Black, false);
                    Grid.SetColumn(light, 1);
                    tile.Children.Add(dark); tile.Children.Add(light);
                    Grid.SetRow(tile, i / cols); Grid.SetColumn(tile, i % cols);
                    grid.Children.Add(tile);
                }
                var window = new Window { Content = grid, Width = 150 * cols + 40, Height = 110 * rows + 60, ShowInTaskbar = false, Left = -10000 };
                window.Show();
                try
                {
                    grid.Measure(new Size(150 * cols, 110 * rows)); grid.Arrange(new Rect(0, 0, 150 * cols, 110 * rows)); grid.UpdateLayout();
                    var bmp = new RenderTargetBitmap(150 * cols, 110 * rows, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(grid);
                    var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
                    using var fs = System.IO.File.Create(IoPath.Combine(folder, "material-icons-sheet.png"));
                    enc.Save(fs);
                }
                finally { window.Close(); }
            });
        }

        // ---- the PowerShell SVG path converter used by tools/icons/import-material-icons.ps1 ----

        private static string Convert(string d)
        {
            var script = IoPath.Combine(RepoRoot(), "tools", "icons", "SvgPathConverter.ps1");
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \". '{script}'; ConvertTo-Material24Path '{d}'\"")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd().Trim();
            var error = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0 || error.Length > 0) throw new InvalidOperationException(error.Length > 0 ? error : "exit " + p.ExitCode);
            return output;
        }

        private static string RepoRoot()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(IoPath.Combine(dir.FullName, "AvasRoutingTool.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
        }

        [Fact]
        public void Converter_MapsKnownPathsToTheUnit24Box()
        {
            // First 'm' is absolute (translated); relative commands only scale; implicit repeats of m become relative l.
            Assert.Equal("M9.55 15.15 l8.475 -8.475", Convert("m382-354 339-339"));
            // Absolute: x / 40, (y + 960) / 40; arc radii and the end point scale, flags and rotation do not.
            Assert.Equal("M1 1A2 2 0 0 1 3 1H4V3z", Convert("M40-920A80 80 0 0 1 120-920H160V-840z"));
            // V absolute, h and c relative, compact arc flags.
            Assert.Equal("M0 0V24h1c0 1 1 1 1 2a1 1 0 0 1 0.025 1", Convert("M0-960V0h40c0 40 40 40 40 80a40 40 0 011 40"));
        }

        [Fact]
        public void Converter_FailsLoudlyOnUnknownCommands()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Convert("M0 0X1 1"));
            Assert.Contains("Unsupported SVG path command", ex.Message);
        }
    }
}
