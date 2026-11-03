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
    /// <summary>A1: the Advanced tab is gated by ShowAdvanced (default off), wired like ShowLogs.</summary>
    [Collection("WpfSta")]
    public class UiV2AdvancedTabTests
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
                try { TestResources.Ensure(); body(); }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("Advanced tab failure: " + failure);
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
            throw new FileNotFoundException("appsettings.json not found above " + AppContext.BaseDirectory);
        }

        [Fact]
        public void ShowAdvanced_DefaultsToFalse_InCodeAndShippedSettings()
        {
            Assert.False(new AppConfig().ShowAdvanced);
            using var doc = JsonDocument.Parse(File.ReadAllText(ShippedAppSettingsPath()));
            Assert.False(doc.RootElement.GetProperty("ShowAdvanced").GetBoolean());
        }

        [Fact]
        public void AdvancedTab_IsHiddenWhenOff_ShownWhenOn_AfterLogs() => RunSta(() =>
        {
            var w = new MainWindow();
            try
            {
                var vm = (MainViewModel)w.DataContext;
                var tab = (RadioButton)w.FindName("TabAdvanced");
                var logs = (RadioButton)w.FindName("TabLogs");
                Assert.NotNull(tab);
                vm.ShowAdvanced = false;
                Assert.Equal(Visibility.Collapsed, tab.Visibility);
                vm.ShowAdvanced = true;
                Assert.Equal(Visibility.Visible, tab.Visibility);
                var panel = (Panel)logs.Parent;
                Assert.True(panel.Children.IndexOf(tab) > panel.Children.IndexOf(logs));
                Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(tab)));
            }
            finally { w.Close(); }
        });

        [Fact]
        public void DisablingAdvanced_WhileOnTheTab_ReturnsToVisualRouting() => RunSta(() =>
        {
            var w = new MainWindow();
            try
            {
                var vm = (MainViewModel)w.DataContext;
                vm.ShowAdvanced = true;
                ((RadioButton)w.FindName("TabAdvanced")).IsChecked = true;
                Assert.Equal(Visibility.Visible, ((FrameworkElement)w.FindName("ViewAdvanced")).Visibility);
                vm.ShowAdvanced = false;
                Assert.True(((RadioButton)w.FindName("TabVisualRouting")).IsChecked);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)w.FindName("ViewAdvanced")).Visibility);
            }
            finally { w.Close(); }
        });

        [Fact]
        public void SettingsDialog_HasAShowAdvancedToggle_OffByDefault_AndPersistsIt() => RunSta(() =>
        {
            var cfg = new MemoryConfig();
            var d = new SettingsDialog(cfg);
            try
            {
                var box = (System.Windows.Controls.Primitives.ToggleButton)d.FindName("ChkShowAdvanced");
                Assert.NotNull(box);
                Assert.NotEqual(true, box.IsChecked);
                Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(box)));
                box.IsChecked = true;
                typeof(SettingsDialog).GetMethod("SaveSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(d, null);
                Assert.True(cfg.Config.ShowAdvanced);
            }
            finally { d.Close(); }
        });
    }
}
