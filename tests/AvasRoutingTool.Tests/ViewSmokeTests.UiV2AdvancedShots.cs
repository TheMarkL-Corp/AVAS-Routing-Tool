using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Services;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Screenshots of the Advanced tab and the link switch dialog (on demand): set AVAS_UIV2_SHOTS to the output folder and run
    /// <c>--filter FullyQualifiedName~UiV2_AdvancedScreenshots</c>. Without the variable the test does nothing.
    /// </summary>
    public partial class ViewSmokeTests
    {
        [Fact]
        public void UiV2_AdvancedScreenshots()
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
                        var suffix = $"{(int)w}x{(int)h}-{(light ? "light" : "dark")}";
                        var mode = light ? ThemeMode.Light : ThemeMode.Dark;

                        var window = new MainWindow { WindowState = WindowState.Normal, Width = w, Height = h, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
                        window.Show();
                        try
                        {
                            themeService.SetTheme(mode);
                            var vm = (MainViewModel)window.DataContext;
                                                        vm.ShowAdvanced = true;
                            // The sample page has its own view model: the window's own discovery must not replace these rows.
                            var advanced = new AdvancedViewModel();
                            ((FrameworkElement)window.FindName("ViewAdvanced")).DataContext = advanced;
                            advanced.SetDevices(new[]
                            {
                                // TX2: a healthy dual-link pair (one row for both chips). TX4: chip 1 offline. TX3: chip 1 not found.
                                new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = "TX2_DUAL LINK_SFPA", IsTransmitter = true },
                                new AvasDevice { DeviceId = "74fe488b07bc", DeviceName = "TX2_DUAL LINK_SFPB", IsTransmitter = true, ChipId = 1 },
                                new AvasDevice { DeviceId = "74fe488b0a01", DeviceName = "TX3_1080P_SFPA", IsTransmitter = true },
                                new AvasDevice { DeviceId = "74fe488b0c01", DeviceName = "TX4_DUAL LINK_SFPA", IsTransmitter = true },
                                new AvasDevice { DeviceId = "74fe488b0c02", DeviceName = "TX4_DUAL LINK_SFPB", IsTransmitter = true, ChipId = 1, IsActive = false },
                                new AvasDevice { DeviceId = "0016c0589c1e", DeviceName = "VEGA camera", IsTransmitter = true, VendorId = 7 },
                                new AvasDevice { DeviceId = "74fe483c059e", DeviceName = "RX1_1080P_SFPA", IsReceiver = true },
                                new AvasDevice { DeviceId = "74fe483c05a1", DeviceName = "RX3_1080P_SFPB", IsReceiver = true, IsActive = false },
                            });
                            AdvancedDeviceRowViewModel Row(string name) => advanced.Rows.First(r => r.Name == name);
                            var tx2 = Row("TX2_DUAL LINK_SFPA");
                            tx2.ApplyTemperature(58); tx2.ApplyCompanionTemperature(64);
                            tx2.ApplyLinkMode(new LinkModeInfo(LinkMode.Dual, "74fe488b07bc", "SINGLE,DUAL"));
                            var tx3 = Row("TX3_1080P_SFPA");
                            tx3.ApplyTemperature(null); tx3.ApplyLinkMode(new LinkModeInfo(LinkMode.Single, "NONE", "SINGLE,DUAL"));
                            var tx4 = Row("TX4_DUAL LINK_SFPA");
                            tx4.ApplyTemperature(61); tx4.ApplyCompanionTemperature(null);
                            tx4.ApplyLinkMode(new LinkModeInfo(LinkMode.Single, "NONE", "SINGLE,DUAL"));
                            Row("RX1_1080P_SFPA").ApplyTemperature(73);
                            Row("RX3_1080P_SFPB").ApplyTemperature(49);
                            ((RadioButton)window.FindName("TabAdvanced")).IsChecked = true;
                            Relayout(window);
                            SavePng(ClientRoot(window), Path.Combine(folder, $"01-advanced-tab-{suffix}.png"));
                        }
                        finally { window.Close(); }

                        foreach (var (name, impact) in new[]
                        {
                            ("02-link-switch-routed", new LinkSwitchImpact(new[] { "RX1_1080P_SFPA" }, new[] { "RX3_1080P_SFPB" })),
                            ("03-link-switch-idle", LinkSwitchImpact.None)
                        })
                        {
                            var dialog = new LinkSwitchDialog(new LinkSwitchRequest("TX2_DUAL LINK_SFPA", LinkMode.Single, impact))
                            { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = 0 };
                            dialog.Show();
                            try { themeService.SetTheme(mode); Relayout(dialog); SavePng(ClientRoot(dialog), Path.Combine(folder, $"{name}-{suffix}.png")); }
                            finally { dialog.Close(); }
                        }
                    }
                }
                finally { themeService.SetTheme(ThemeMode.Dark); }
            });
        }
    }
}
