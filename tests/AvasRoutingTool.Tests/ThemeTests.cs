using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace AvasRoutingTool.Tests
{
    [Collection("WpfSta")]
    public class ThemeTests
    {
        private static readonly object AppLock = new();

        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    lock (AppLock)
                    {
                        if (Application.Current is null)
                        {
                            try
                            {
                                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                            }
                            catch (InvalidOperationException)
                            {
                                // Application already initialized in this AppDomain
                            }
                        }
                    }
                    body();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure is not null)
            {
                throw new AggregateException(failure);
            }
        }

        [Fact]
        public void ThemeDictionaries_DarkAndLight_ContainIdenticalKeysAndValidBrushes() =>
            RunSta(() =>
            {
                var darkUri = new Uri("pack://application:,,,/AvasRoutingTool;component/Resources/Themes/Theme.Dark.xaml");
                var lightUri = new Uri("pack://application:,,,/AvasRoutingTool;component/Resources/Themes/Theme.Light.xaml");

                var dark = new ResourceDictionary { Source = darkUri };
                var light = new ResourceDictionary { Source = lightUri };

                Assert.True(dark.Keys.Count > 0, "Theme.Dark has no keys");
                Assert.True(light.Keys.Count > 0, "Theme.Light has no keys");

                var darkKeys = dark.Keys.Cast<object>().Select(k => k.ToString()).OrderBy(k => k).ToList();
                var lightKeys = light.Keys.Cast<object>().Select(k => k.ToString()).OrderBy(k => k).ToList();

                foreach (var key in darkKeys)
                {
                    Assert.True(light.Contains(key), $"Theme.Light is missing key: '{key}' found in Theme.Dark");
                    var darkVal = dark[key];
                    var lightVal = light[key];
                    Assert.Equal(darkVal.GetType(), lightVal.GetType());
                }

                foreach (var key in lightKeys)
                {
                    Assert.True(dark.Contains(key), $"Theme.Dark is missing key: '{key}' found in Theme.Light");
                }

                // Verify Dark palette is truly dark (Background luminance < 0.25)
                var darkBg = (SolidColorBrush)dark["Brush.Background.Dark"];
                double darkLum = (0.299 * darkBg.Color.R + 0.587 * darkBg.Color.G + 0.114 * darkBg.Color.B) / 255.0;
                Assert.True(darkLum < 0.25, $"Dark background luminance ({darkLum}) is too bright");

                // Verify Light palette is truly light (Background luminance > 0.85)
                var lightBg = (SolidColorBrush)light["Brush.Background.Dark"];
                double lightLum = (0.299 * lightBg.Color.R + 0.587 * lightBg.Color.G + 0.114 * lightBg.Color.B) / 255.0;
                Assert.True(lightLum > 0.85, $"Light background luminance ({lightLum}) is too dark");

                // Verify text contrast in light mode: primary text against card surface
                var lightCard = (SolidColorBrush)light["Brush.Surface.Card"];
                var lightText = (SolidColorBrush)light["Brush.Text.Primary"];
                double cardLum = (0.299 * lightCard.Color.R + 0.587 * lightCard.Color.G + 0.114 * lightCard.Color.B) / 255.0;
                double textLum = (0.299 * lightText.Color.R + 0.587 * lightText.Color.G + 0.114 * lightText.Color.B) / 255.0;
                Assert.True(cardLum - textLum > 0.6, "Light mode primary text does not have high contrast against card surface");
            });

        private static double Linear(byte c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        private static double Luminance(double r, double g, double b) =>
            0.2126 * Linear((byte)Math.Round(r)) + 0.7152 * Linear((byte)Math.Round(g)) + 0.0722 * Linear((byte)Math.Round(b));

        /// <summary>WCAG contrast ratio; a translucent background is composited over <paramref name="canvas"/> first.</summary>
        private static double Contrast(Color fg, Color bg, Color canvas)
        {
            var a = bg.A / 255.0;
            var lb = Luminance(bg.R * a + canvas.R * (1 - a), bg.G * a + canvas.G * (1 - a), bg.B * a + canvas.B * (1 - a));
            var lf = Luminance(fg.R, fg.G, fg.B);
            var hi = Math.Max(lf, lb);
            var lo = Math.Min(lf, lb);
            return (hi + 0.05) / (lo + 0.05);
        }

        [Fact]
        public void ThemePalettes_MeetWcagContrastRequirements() =>
            RunSta(() =>
            {
                var pairs = new (string Fg, string Bg, double Min)[]
                {
                    ("Color.Text.OnDropTarget", "Color.Border.DropTarget", 4.5),
                    ("Color.Status.Online", "Color.Surface.OnlineBadge", 4.5),
                    ("Color.Text.OnRoutedBadge", "Color.Surface.RoutedBadge", 4.5),
                    ("Color.Text.OnPreviewMuted", "Color.Surface.PreviewBackdrop", 4.5),
                    ("Color.Text.OnAction", "Color.Accent.Primary", 4.5),
                };

                var failures = new System.Collections.Generic.List<string>();
                foreach (var name in new[] { "Dark", "Light" })
                {
                    var dict = new ResourceDictionary
                    {
                        Source = new Uri($"pack://application:,,,/AvasRoutingTool;component/Resources/Themes/Theme.{name}.xaml")
                    };
                    var canvas = dict.Contains("Color.Surface.Card") ? (Color)dict["Color.Surface.Card"] : Colors.White;
                    foreach (var (fg, bg, min) in pairs)
                    {
                        if (!dict.Contains(fg) || !dict.Contains(bg))
                        {
                            failures.Add($"{name}: missing {fg} or {bg}");
                            continue;
                        }
                        var ratio = Contrast((Color)dict[fg], (Color)dict[bg], canvas);
                        if (ratio < min) failures.Add($"{name}: {fg} on {bg} = {ratio:0.00}:1 (< {min})");
                    }
                }

                Assert.True(failures.Count == 0, string.Join("\n", failures));
            });
    }
}
