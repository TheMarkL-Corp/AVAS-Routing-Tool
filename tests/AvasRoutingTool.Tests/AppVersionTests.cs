using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>The app version (1.0.0) must be the same in the .exe details, the app bar and the Settings window.</summary>
    [Collection("WpfSta")]
    public class AppVersionTests
    {
        private sealed class StubConfig : IConfigService
        {
            public AppConfig Load() => new AppConfig();
            public void Save(AppConfig config) { }
        }

        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    foreach (var path in new[] { "Themes/Theme.Dark", "Theme", "Icons" })
                    {
                        app.Resources.MergedDictionaries.Add(new ResourceDictionary
                        {
                            Source = new Uri($"pack://application:,,,/AvasRoutingTool;component/Resources/{path}.xaml")
                        });
                    }
                    body();
                }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("Version UI failure: " + failure);
        }

        [Fact]
        public void AppInfo_Version_IsOneZeroZero() => Assert.Equal("1.0.0", AppInfo.Version);

        [Fact]
        public void ExeFileDetails_ShowTheSameVersion()
        {
            var asm = typeof(AppInfo).Assembly;
            var info = FileVersionInfo.GetVersionInfo(asm.Location);
            Assert.Equal("1.0.0.0", info.FileVersion);
            Assert.StartsWith("1.0.0", info.ProductVersion);
            Assert.Equal("1.0.0.0", asm.GetName().Version?.ToString());
        }

        [Fact]
        public void AppBar_ShowsTheVersionNextToTheAppName() => RunSta(() =>
        {
            var window = new MainWindow { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
            window.Show();
            window.UpdateLayout();
            try
            {
                var label = (TextBlock)window.FindName("AppVersionText");
                Assert.Equal("v1.0.0", label.Text);
            }
            finally { window.Close(); }
        });

        [Fact]
        public void SettingsWindow_ShowsTheVersion() => RunSta(() =>
        {
            var dialog = new SettingsDialog(new StubConfig());
            var label = (TextBlock)dialog.FindName("VersionText");
            Assert.Contains("1.0.0", label.Text);
        });
    }
}
