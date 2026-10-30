using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Services;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public partial class ViewSmokeTests
    {
        /// <summary>
        /// UI v2 checkpoint 3 harness (manual): renders the Composer in 5 states x 2 window sizes x 2 themes to PNG files and writes
        /// the measured layout numbers. Does nothing (and asserts nothing) unless the environment variable UIV2_SHOTS names the output folder.
        /// No hardware: the same in-memory rig as the other Composer view tests.
        /// </summary>
        [Fact]
        public void Composer_CheckpointShots_WhenRequested() =>
            RunSta(() =>
            {
                var folder = Environment.GetEnvironmentVariable("UIV2_SHOTS");
                if (string.IsNullOrWhiteSpace(folder)) return;
                Directory.CreateDirectory(folder);
                var measures = new System.Text.StringBuilder();
                var themeService = new ThemeService();
                try
                {
                    foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light })
                    {
                        themeService.SetTheme(mode);
                        foreach (var (w, h, size) in new[] { (1904, 940, "1920x1080"), (1008, 615, "1024x720") })
                        {
                            foreach (var state in new[] { "A-step1", "B-staged-armed", "C-pip-strip", "D-picker", "E-after-take" })
                            {
                                var (vm, view) = ShotState(state);
                                themeService.SetTheme(mode); // building the overview resets the palette to the saved theme: set it again, last
                                var name = $"{state}_{size}_{mode}".ToLowerInvariant();
                                ShowingAt(view, w, h, () =>
                                {
                                    Relayout(view);
                                    Save(view, w, h, Path.Combine(folder, name + ".png"));
                                    Measure(measures, name, view);
                                });
                            }
                        }
                    }
                }
                finally
                {
                    themeService.SetTheme(ThemeMode.Dark);
                }
                File.WriteAllText(Path.Combine(folder, "measurements.txt"), measures.ToString());
            });

        private static (MainViewModel Vm, VisualRoutingView View) ShotState(string state)
        {
            var (vm, rx, sources) = Overview();
            for (int i = 0; i < sources.Length; i++) sources[i].PreviewBitmap = ShotPicture(i);
            Pump(() => vm.OpenComposerAsync(rx, state == "A-step1" ? null : state == "C-pip-strip" ? "pip" : "matrix2x2"));
            var c = vm.ActiveComposer!;
            switch (state)
            {
                case "B-staged-armed":
                    c.Arm(sources[0]); c.TapBlock(0);
                    c.Arm(sources[1]);        // still armed: the other blocks offer "+ Place"
                    break;
                case "C-pip-strip":
                    c.Arm(sources[0]); c.TapBlock(0); c.Disarm();
                    c.TapBlock(0);            // the inset (window 0): its Replace / Move strip opens
                    break;
                case "D-picker":
                    c.Arm(sources[0]); c.TapBlock(0); c.Disarm();
                    c.TapBlock(1);            // an empty block: the picker opens
                    break;
                case "E-after-take":
                    c.Arm(sources[0]); c.TapBlock(c.Blocks[0].Index); c.Disarm();
                    Pump(c.TakeAsync);
                    c.Arm(sources[1]); c.TapBlock(c.Blocks[1].Index); c.Disarm();
                    Pump(c.TakeAsync);        // success banner + Undo
                    break;
            }
            return (vm, new VisualRoutingView { DataContext = vm });
        }

        private static WriteableBitmap ShotPicture(int seed)
        {
            var bmp = new WriteableBitmap(160, 90, 96, 96, PixelFormats.Bgr24, null);
            var pixels = new byte[160 * 90 * 3];
            for (int y = 0; y < 90; y++)
            {
                for (int x = 0; x < 160; x++)
                {
                    int i = (y * 160 + x) * 3;
                    pixels[i] = (byte)(40 + x + seed * 30);
                    pixels[i + 1] = (byte)(60 + y * 2);
                    pixels[i + 2] = (byte)(90 + seed * 40 + (x ^ y) % 20);
                }
            }
            bmp.WritePixels(new Int32Rect(0, 0, 160, 90), pixels, 160 * 3, 0);
            return bmp;
        }

        private static void Save(FrameworkElement view, int w, int h, string path)
        {
            // The window chrome is not drawn: the picture is the content area, as the operator sees it under the app bar.
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            var fill = new DrawingVisual();
            using (var dc = fill.RenderOpen()) dc.DrawRectangle((Brush)Application.Current.FindResource("Brush.Background.Dark"), null, new Rect(0, 0, w, h));
            bitmap.Render(fill);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        private static void Measure(System.Text.StringBuilder sb, string name, FrameworkElement view)
        {
            string Box(string element)
            {
                var e = Named(view, element).FirstOrDefault(x => x.IsVisible);
                return e == null ? "-" : $"{e.ActualWidth:F0}x{e.ActualHeight:F0}";
            }
            var composer = FindAll<MultiViewComposerView>(view).FirstOrDefault(v => v.IsVisible);
            string rails = "-";
            if (composer != null)
            {
                var left = (ColumnDefinition)composer.FindName("LeftRailColumn");
                var right = (ColumnDefinition)composer.FindName("RightRailColumn");
                rails = $"left {left.ActualWidth:F0} / right {right.ActualWidth:F0}";
            }
            var tiles = FindAll<LayoutSchematic>(view).Where(s => s.IsVisible).Select(s => $"{s.ActualWidth:F0}x{s.ActualHeight:F0}");
            var blocks = Named(view, "BlockSurface").Where(b => b.IsVisible).Select(b => $"{b.ActualWidth:F0}x{b.ActualHeight:F0}");
            var palette = FindAll<CompactSourceCardView>(view).Where(p => p.IsVisible).Select(p => $"{p.ActualWidth:F0}x{p.ActualHeight:F0}").FirstOrDefault() ?? "-";
            var strip = Named(view, "CompactSourcePalette").FirstOrDefault(p => p.IsVisible);
            sb.AppendLine($"{name}: rails {rails}; canvas host {Box("CanvasHost")}; layout tiles [{string.Join(", ", tiles)}]; blocks [{string.Join(", ", blocks)}]; " +
                          $"take {Box("TakeButton")}; palette card {palette}; palette strip {(strip == null ? "-" : $"{strip.ActualHeight:F0} high")}");
        }
    }
}
