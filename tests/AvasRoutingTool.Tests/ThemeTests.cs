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
    }
}
