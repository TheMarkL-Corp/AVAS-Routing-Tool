using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Logging;
using AvasRoutingTool.Services;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Checkpoint screenshots of the Main Window part (on demand): set AVAS_UIV2_SHOTS to the output folder and run
    /// <c>--filter FullyQualifiedName~UiV2_MainWindowScreenshots</c>. Without the variable the test does nothing.
    /// The real views are rendered off-screen with the sample data of the view tests (a WebView2 surface is not captured).
    /// </summary>
    public partial class ViewSmokeTests
    {
        private sealed class ShotConfig : IConfigService
        {
            public AppConfig Load() => new AppConfig { ShowLogs = true };
            public void Save(AppConfig config) { }
        }

        /// <summary>The window's client area (its root decorator: background plus content), without the native title bar.</summary>
        private static FrameworkElement ClientRoot(Window window)
        {
            window.UpdateLayout();
            return (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        }

        private static void SavePng(FrameworkElement element, string path)
        {
            element.UpdateLayout();
            var width = (int)Math.Ceiling(element.ActualWidth);
            var height = (int)Math.Ceiling(element.ActualHeight);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        [Fact]
        public void UiV2_MainWindowScreenshots()
        {
            var folder = Environment.GetEnvironmentVariable("AVAS_UIV2_SHOTS");
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);

            RunSta(() =>
            {
                var themeService = new ThemeService();
                try
                {
                    foreach (var light in new[] { false, true })
                    foreach (var (w, h) in new[] { (1920.0, 1080.0), (1024.0, 720.0) })
                    {
                        themeService.SetTheme(light ? ThemeMode.Light : ThemeMode.Dark);
                        var suffix = $"{(int)w}x{(int)h}-{(light ? "light" : "dark")}";

                        void Scene(string name, Action<CopyRig>? arrange = null, Action<MainWindow, CopyRig>? after = null, Func<MainWindow, FrameworkElement>? target = null)
                        {
                            var rig = new CopyRig();
                            var window = new MainWindow { DataContext = rig.Vm, WindowState = WindowState.Normal, Width = w, Height = h, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
                            window.Show();
                            try
                            {
                                themeService.SetTheme(light ? ThemeMode.Light : ThemeMode.Dark); // the window's own view model resets the theme when it is built
                                arrange?.Invoke(rig);
                                Relayout(window);
                                after?.Invoke(window, rig);
                                Relayout(window);
                                SavePng(target?.Invoke(window) ?? ClientRoot(window), Path.Combine(folder, $"{name}-{suffix}.png"));
                            }
                            finally { window.Close(); }
                        }

                        Scene("01-overview-idle");
                        Scene("02-source-armed", rig => rig.Vm.ArmSource(rig.Tx2));
                        Scene("03-copy-armed-banner", rig => Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1)));
                        Scene("04-display-undo-toast", rig => { Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1)); Pump(() => rig.Vm.CopyDisplayAsync(rig.Rx1, rig.Pax)); rig.Vm.DisarmDisplay(); });
                        Scene("06-drop-choice-panel", rig => rig.Vm.OpenDropChoice(rig.Rx1, rig.Tx2));

                        // The layout menu is a popup (its own surface): render its content next to the card.
                        {
                            var rig = new CopyRig();
                            var window = new MainWindow { DataContext = rig.Vm, WindowState = WindowState.Normal, Width = w, Height = h, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
                            window.Show();
                            try
                            {
                                themeService.SetTheme(light ? ThemeMode.Light : ThemeMode.Dark);
                                Relayout(window);
                                var card = FindAll<DisplayCardView>(window).First(c => c.DataContext == rig.Rx1);
                                rig.Rx1.IsLayoutMenuOpen = true;
                                Relayout(window);
                                var popup = (Popup)Named(card, "LayoutMenuPopup").Single();
                                var menu = (FrameworkElement)popup.Child;
                                Relayout(menu);
                                SavePng(menu, Path.Combine(folder, $"05-layout-menu-{suffix}.png"));
                            }
                            finally { window.Close(); }
                        }

                        // Logs tab (ShowLogs on) and the BAVM Console tab on the real window view model.
                        foreach (var tab in new[] { "07-logs-tab", "08-bavm-console-tab" })
                        {
                            var window = new MainWindow { WindowState = WindowState.Normal, Width = w, Height = h, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
                            window.Show();
                            try
                            {
                                themeService.SetTheme(light ? ThemeMode.Light : ThemeMode.Dark);
                                var vm = (MainViewModel)window.DataContext;
                                vm.ShowLogs = true;
                                vm.Logs.Clear();
                                var start = new DateTime(2026, 10, 30, 9, 41, 12, 120);
                                var sample = new (LogLevel, string, string)[]
                                {
                                    (LogLevel.Info, "APP", "Initializing AVAS Routing Tool with BAVM URL: http://localhost:80, Control Server: 127.0.0.1:6970 (REST: 8080)"),
                                    (LogLevel.Info, "DISCOVERY", "Starting transceiver refresh cycle..."),
                                    (LogLevel.Debug, "PREVIEW", "Started preview for TX2_DUAL LINK_SFPA on 225.0.0.7:5000 (Status: True)"),
                                    (LogLevel.Warn, "ROUTING", "RX3_1080P_SFPA did not confirm FAST_SWITCHED; retrying with the display EDID format"),
                                    (LogLevel.Error, "COMPOSER", "Take refused: the TX scaler size differs between decoders (one stream-1 size per encoder)"),
                                };
                                for (var i = 0; i < 40; i++)
                                {
                                    var (level, cat, msg) = sample[i % sample.Length];
                                    vm.Logs.Add(new LogEntry(start.AddMilliseconds(i * 731), level, cat, msg));
                                }
                                var tabName = tab.StartsWith("07", StringComparison.Ordinal) ? "TabLogs" : "TabBavmConsole";
                                ((RadioButton)window.FindName(tabName)).IsChecked = true;
                                Relayout(window);
                                SavePng(ClientRoot(window), Path.Combine(folder, $"{tab}-{suffix}.png"));
                            }
                            finally { window.Close(); }
                        }

                        // Settings dialog
                        {
                            var dialog = new SettingsDialog(new ShotConfig()) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = 0 };
                            dialog.Show();
                            try { themeService.SetTheme(light ? ThemeMode.Light : ThemeMode.Dark); Relayout(dialog); SavePng(ClientRoot(dialog), Path.Combine(folder, $"09-settings-dialog-{suffix}.png")); }
                            finally { dialog.Close(); }
                        }
                    }
                }
                finally { themeService.SetTheme(ThemeMode.Dark); }
            });
        }
    }
}
