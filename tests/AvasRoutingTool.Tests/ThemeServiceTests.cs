using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Services;
using Xunit;

namespace AvasRoutingTool.Tests
{
    [Collection("WpfSta")]
    public class ThemeServiceTests
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
                                // Already initialized
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
        public void ThemeService_InitializesFromConfig_AndSetsThemeCorrectly() =>
            RunSta(() =>
            {
                string tempFile = Path.Combine(Path.GetTempPath(), $"avas_theme_test_{Guid.NewGuid():N}.json");
                try
                {
                    var configService = new ConfigService(tempFile);
                    var service = new ThemeService(configService);

                    Assert.Equal(ThemeMode.Dark, service.CurrentTheme);

                    service.SetTheme(ThemeMode.Light);
                    Assert.Equal(ThemeMode.Light, service.CurrentTheme);
                    Assert.Equal("Light", configService.Load().Theme);

                    // Toggle should return to Dark
                    service.ToggleTheme();
                    Assert.Equal(ThemeMode.Dark, service.CurrentTheme);
                    Assert.Equal("Dark", configService.Load().Theme);
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            });

        [Fact]
        public void ThemeService_AppliesResourceDictionaryToApplication_WhenSwitched() =>
            RunSta(() =>
            {
                string tempFile = Path.Combine(Path.GetTempPath(), $"avas_theme_test_{Guid.NewGuid():N}.json");
                try
                {
                    var configService = new ConfigService(tempFile);
                    var service = new ThemeService(configService);

                    service.SetTheme(ThemeMode.Light);

                    // Verify application resource Brush.Background.Dark is now light
                    var bgBrush = (SolidColorBrush)Application.Current.FindResource("Brush.Background.Dark");
                    double lum = (0.299 * bgBrush.Color.R + 0.587 * bgBrush.Color.G + 0.114 * bgBrush.Color.B) / 255.0;
                    Assert.True(lum > 0.85, $"Expected light background in Light mode, got lum {lum}");

                    service.SetTheme(ThemeMode.Dark);
                    bgBrush = (SolidColorBrush)Application.Current.FindResource("Brush.Background.Dark");
                    lum = (0.299 * bgBrush.Color.R + 0.587 * bgBrush.Color.G + 0.114 * bgBrush.Color.B) / 255.0;
                    Assert.True(lum < 0.25, $"Expected dark background in Dark mode, got lum {lum}");
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            });
    }
}
