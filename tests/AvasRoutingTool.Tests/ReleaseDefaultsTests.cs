using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>Release defaults: BAVM http://localhost:80, REST port 8080, opens maximized, Logs hidden until enabled in Settings.</summary>
    [Collection("WpfSta")]
    public class ReleaseDefaultsTests
    {
        private sealed class MemoryConfig : IConfigService
        {
            public AppConfig Config = new AppConfig();
            public AppConfig Load() => Config;
            public void Save(AppConfig config) => Config = config;
        }

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
            if (failure != null) throw new Xunit.Sdk.XunitException("Release defaults failure: " + failure);
        }

        private static string ShippedAppSettingsPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "AvasRoutingTool", "appsettings.json");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("src/AvasRoutingTool/appsettings.json not found above " + AppContext.BaseDirectory);
        }

        [Fact]
        public void CodeDefaults_AreTheReleaseDefaults()
        {
            var c = new AppConfig();
            Assert.Equal("http://localhost:80", c.BavmUrl);
            Assert.Equal(8080, c.RestPort);
            Assert.False(c.ShowLogs);
        }

        [Fact]
        public void ShippedAppSettings_AreTheReleaseDefaults()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(ShippedAppSettingsPath()));
            var root = doc.RootElement;
            Assert.Equal("http://localhost:80", root.GetProperty("BavmUrl").GetString());
            Assert.Equal(8080, root.GetProperty("RestPort").GetInt32());
            Assert.False(root.GetProperty("ShowLogs").GetBoolean());
        }

        [Fact]
        public void MainWindow_OpensMaximized_WithNormalChrome() => RunSta(() =>
        {
            var w = new MainWindow();
            try
            {
                Assert.Equal(WindowState.Maximized, w.WindowState);
                Assert.Equal(WindowStyle.SingleBorderWindow, w.WindowStyle);
                Assert.Equal(ResizeMode.CanResize, w.ResizeMode);
                Assert.False(w.Topmost);
            }
            finally { w.Close(); }
        });

        [Fact]
        public void LogsTab_IsHiddenByDefault_AndFollowsTheShowLogsSetting() => RunSta(() =>
        {
            var w = new MainWindow();
            try
            {
                var vm = (MainViewModel)w.DataContext;
                var tab = (RadioButton)w.FindName("TabLogs");
                vm.ShowLogs = false;
                Assert.Equal(Visibility.Collapsed, tab.Visibility);
                vm.ShowLogs = true;
                Assert.Equal(Visibility.Visible, tab.Visibility);
            }
            finally { w.Close(); }
        });

        [Fact]
        public void DisablingLogs_WhileOnTheLogsTab_ReturnsToVisualRouting() => RunSta(() =>
        {
            var w = new MainWindow();
            try
            {
                var vm = (MainViewModel)w.DataContext;
                vm.ShowLogs = true;
                ((RadioButton)w.FindName("TabLogs")).IsChecked = true;
                vm.ShowLogs = false;
                Assert.True(((RadioButton)w.FindName("TabVisualRouting")).IsChecked);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)w.FindName("ViewLogs")).Visibility);
            }
            finally { w.Close(); }
        });

        [Fact]
        public void SettingsDialog_HasAShowLogsToggle_OffByDefault_AndPersistsIt() => RunSta(() =>
        {
            var cfg = new MemoryConfig();
            var d = new SettingsDialog(cfg);
            try
            {
                var box = (CheckBox)d.FindName("ChkShowLogs");
                Assert.NotNull(box);
                Assert.NotEqual(true, box.IsChecked);
                Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(box)));
                box.IsChecked = true;
                typeof(SettingsDialog).GetMethod("SaveSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(d, null);
                Assert.True(cfg.Config.ShowLogs);
            }
            finally { d.Close(); }
        });
    }
}
