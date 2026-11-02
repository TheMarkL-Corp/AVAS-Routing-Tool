using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        /// UI v2 Composer round 2 harness (manual): renders the Composer in 6 states x 2 window sizes x 2 themes (24 PNGs) from
        /// colour-bar source frames, writes the measured layout numbers to measurements.txt and the round 1 vs round 2 table to BEFORE-AFTER.md.
        /// Does nothing (and asserts nothing) unless the environment variable UIV2_SHOTS names the output folder.
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
                var measured = new Dictionary<string, Dictionary<string, string>>();
                var themeService = new ThemeService();
                try
                {
                    foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light })
                    {
                        themeService.SetTheme(mode);
                        foreach (var (w, h, size) in new[] { (1904, 940, "1920x1080"), (1008, 615, "1024x720") })
                        {
                            foreach (var state in new[] { "A-step1", "B-staged-armed", "C-pip-strip", "D-picker", "E-after-take", "F-step1-hover-pip" })
                            {
                                var (vm, view) = ShotState(state);
                                themeService.SetTheme(mode); // building the overview resets the palette to the saved theme: set it again, last
                                var name = $"{state}_{size}_{mode}".ToLowerInvariant();
                                ShowingAt(view, w, h, () =>
                                {
                                    ApplyShotInteraction(state, view);
                                    Relayout(view);
                                    Save(view, w, h, Path.Combine(folder, name + ".png"));
                                    var fields = Measure(view);
                                    measures.AppendLine($"{name}: " + string.Join("; ", fields.Select(f => $"{f.Key} {f.Value}")));
                                    measured[name] = fields.ToDictionary(f => f.Key, f => f.Value);
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
                WriteBeforeAfter(folder, measured);
            });

        [Fact]
        public void Composer_ShotPicture_IsAColourBarSignal() =>
            RunSta(() =>
            {
                var bars = ShotPicture(0);
                Assert.Equal(160, bars.PixelWidth);
                Assert.Equal(90, bars.PixelHeight);

                var white = PixelAt(bars, 5, 0);   // first bar: white
                Assert.True(white.R > 150 && white.G > 150 && white.B > 150, $"bar 1 at x=5 is {white}");

                var blue = PixelAt(bars, 150, 0);  // last of the seven bars: blue
                Assert.True(blue.B > 150 && blue.R < 80, $"bar 7 at x=150 is {blue}");

                var dimmed = PixelAt(ShotPicture(3), 5, 0);
                Assert.True(dimmed.R + dimmed.G + dimmed.B < white.R + white.G + white.B, $"seed 3 at x=5 is {dimmed}, seed 0 is {white}");
            });

        private static (int R, int G, int B) PixelAt(BitmapSource bitmap, int x, int y)
        {
            var bgr = new byte[3];
            bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), bgr, 3, 0);
            return (bgr[2], bgr[1], bgr[0]);
        }

        private static (MainViewModel Vm, VisualRoutingView View) ShotState(string state)
        {
            var (vm, rx, sources) = Overview();
            for (int i = 0; i < sources.Length; i++) sources[i].PreviewBitmap = ShotPicture(i);
            Pump(() => vm.OpenComposerAsync(rx, (state is "A-step1" or "F-step1-hover-pip") ? null : state == "C-pip-strip" ? "pip" : "matrix2x2"));
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

        /// <summary>The interactions a shot needs on the shown view (the hover preview of the pip tile); nothing for the other states.</summary>
        private static void ApplyShotInteraction(string state, FrameworkElement view)
        {
            if (state != "F-step1-hover-pip") return;
            var pip = Named(view, "LayoutTile").Cast<Button>().Single(t => t.IsVisible && ((ComposerLayoutOptionViewModel)t.DataContext).Id == "pip");
            pip.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
        }

        /// <summary>
        /// Source frame for the shots: SMPTE-style colour bars (seven bars at 75 %, a reverse-bar strip, a black/white/black block row),
        /// so a block reads as a signal; the old gradient read as decoration. The seed dims the whole frame (factors 1.0, 0.8, 0.6, 0.45;
        /// the last holds for larger seeds) so the blocks of different sources stay distinguishable.
        /// </summary>
        private static WriteableBitmap ShotPicture(int seed)
        {
            const int width = 160, height = 90;
            const int barRows = 60, stripRows = 8; // the top two thirds, then about a twelfth
            const byte full = 191;                 // 75 % intensity
            (byte R, byte G, byte B)[] bars =
            {
                (full, full, full), (full, full, 0), (0, full, full), (0, full, 0),
                (full, 0, full), (full, 0, 0), (0, 0, full),
            };
            (byte R, byte G, byte B)[] strip =
            {
                (0, 0, full), (0, 0, 0), (full, 0, full), (0, 0, 0), (0, full, full), (0, 0, 0), (full, full, full),
            };
            (byte R, byte G, byte B)[] bottom = { (0, 0, 0), (full, full, full), (0, 0, 0) };
            double dim = new[] { 1.0, 0.8, 0.6, 0.45 }[Math.Min(seed, 3)];

            var pixels = new byte[width * height * 3];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var (r, g, b) = y < barRows ? bars[x * 7 / width]
                        : y < barRows + stripRows ? strip[x * 7 / width]
                        : bottom[x * 3 / width];
                    int i = (y * width + x) * 3;
                    pixels[i] = (byte)Math.Round(b * dim);
                    pixels[i + 1] = (byte)Math.Round(g * dim);
                    pixels[i + 2] = (byte)Math.Round(r * dim);
                }
            }
            var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);
            bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 3, 0);
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

        /// <summary>The measured layout numbers of one shot, as (key, value) pairs in the order measurements.txt prints them.</summary>
        private static List<(string Key, string Value)> Measure(FrameworkElement view)
        {
            string Box(string element)
            {
                var e = Named(view, element).FirstOrDefault(x => x.IsVisible);
                return e == null ? "-" : $"{e.ActualWidth:F0}x{e.ActualHeight:F0}";
            }
            string At(string element)
            {
                var e = Named(view, element).FirstOrDefault(x => x.IsVisible);
                if (e == null) return "-";
                var at = BoundsIn(e, view);
                return $"{e.ActualWidth:F0}x{e.ActualHeight:F0} at {at.X:F0},{at.Y:F0}";
            }
            var composer = FindAll<MultiViewComposerView>(view).FirstOrDefault(v => v.IsVisible);
            string rails = "-";
            if (composer != null)
            {
                var left = (ColumnDefinition)composer.FindName("LeftRailColumn");
                var right = (ColumnDefinition)composer.FindName("RightRailColumn");
                rails = $"left {left.ActualWidth:F0} / right {right.ActualWidth:F0}";
            }
            var tiles = FindAll<LayoutSchematic>(view).Where(s => s.IsVisible && s.Name != "GhostSchematic").Select(s => $"{s.ActualWidth:F0}x{s.ActualHeight:F0}");
            var blocks = Named(view, "BlockSurface").Where(b => b.IsVisible).Select(b => $"{b.ActualWidth:F0}x{b.ActualHeight:F0}");
            var palette = FindAll<SourceCardView>(view).Where(p => p.IsVisible && p.IsCompact).Select(p => $"{p.ActualWidth:F0}x{p.ActualHeight:F0}").FirstOrDefault() ?? "-";
            var strip = Named(view, "CompactSourcePalette").FirstOrDefault(p => p.IsVisible);
            var rail = Named(view, "RightRailScroll").OfType<ScrollViewer>().FirstOrDefault(s => s.IsVisible);
            return new()
            {
                ("rails", rails),
                ("ghost schematic", Box("GhostSchematic")),
                ("canvas host", Box("CanvasHost")),
                ("layout tiles", $"[{string.Join(", ", tiles)}]"),
                ("blocks", $"[{string.Join(", ", blocks)}]"),
                ("step 2 heading", At("StepPlaceHeading")),
                ("step 3 heading", Box("StepTakeHeading")),
                ("changes card", Box("ChangesCard")),
                ("right rail", rail == null ? "-" : $"viewport {rail.ViewportHeight:F0}, scrollable {rail.ScrollableHeight:F0}"),
                ("take", Box("TakeButton")),
                ("palette card", palette),
                ("palette strip", strip == null ? "-" : $"{strip.ActualHeight:F0} high"),
            };
        }

        /// <summary>
        /// Round 1 numbers for the states BEFORE-AFTER.md compares (dark theme), copied from
        /// ui_screenshots/UI-v2/composer/measurements.txt in the main checkout (round 1 pictures were a gradient).
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, string>> Round1 = new()
        {
            ["a-step1_1920x1080_dark"] = new() { ["rails"] = "left 320 / right 360", ["canvas host"] = "1144x635", ["layout tiles"] = "[127x74, 127x74, 127x74, 127x74]", ["take"] = "360x72", ["palette card"] = "340x99" },
            ["b-staged-armed_1920x1080_dark"] = new() { ["rails"] = "left 320 / right 360", ["canvas host"] = "1144x635", ["layout tiles"] = "[126x74, 127x74, 127x74, 127x74]", ["take"] = "360x72", ["palette card"] = "340x99" },
            ["a-step1_1024x720_dark"] = new() { ["rails"] = "left 240 / right 264", ["canvas host"] = "424x310", ["layout tiles"] = "[90x54, 90x54, 90x54, 90x54]", ["take"] = "264x72", ["palette card"] = "340x99" },
            ["b-staged-armed_1024x720_dark"] = new() { ["rails"] = "left 240 / right 264", ["canvas host"] = "424x310", ["layout tiles"] = "[90x53, 90x54, 90x54, 90x54]", ["take"] = "264x72", ["palette card"] = "340x99" },
        };

        private static readonly string[] Compared = { "rails", "canvas host", "layout tiles", "take", "palette card" };

        private static void WriteBeforeAfter(string folder, Dictionary<string, Dictionary<string, string>> measured)
        {
            var md = new System.Text.StringBuilder();
            md.AppendLine("# Composer shots: round 1 vs round 2");
            md.AppendLine();
            md.AppendLine("Picture source: the round 2 shots use SMPTE-style colour bars (round 1 used a synthetic gradient), so the source frames read as a signal.");
            md.AppendLine();
            md.AppendLine("Dark theme, states a-step1 and b-staged-armed at 1920x1080 and 1024x720. Round 1 is ui_screenshots/UI-v2/composer/measurements.txt in the main checkout.");
            md.AppendLine();
            md.AppendLine("| Shot | Element | Round 1 | Round 2 |");
            md.AppendLine("|---|---|---|---|");
            foreach (var shot in Round1.Keys)
                foreach (var element in Compared)
                    md.AppendLine($"| {shot} | {element} | {Round1[shot][element]} | {measured[shot][element]} |");
            File.WriteAllText(Path.Combine(folder, "BEFORE-AFTER.md"), md.ToString());
        }
    }
}
