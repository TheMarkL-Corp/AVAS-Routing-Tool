using System;
using System.IO;
using System.Threading.Tasks;
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
    /// Stage 2 screenshots (on demand): the Advanced tab with its Danger zone and the confirmation dialogs. Set AVAS_UIV2_SHOTS
    /// to the output folder and run <c>--filter FullyQualifiedName~UiV2_AdvancedStage2Screenshots</c>.
    /// </summary>
    public partial class ViewSmokeTests
    {
        private sealed class NoOpMaintenance : IMaintenanceActions
        {
            public Task<bool> RebootChipAsync(AvasDevice device, string mac) => Task.FromResult(true);
            public Task<bool> RebootMcuAsync(AvasDevice device) => Task.FromResult(true);
            public Task<bool> FactoryResetMcuAsync(AvasDevice device) => Task.FromResult(true);
            public Task<bool> FactoryResetChipAsync(AvasDevice device, string mac) => Task.FromResult(true);
        }

        [Fact]
        public void UiV2_AdvancedStage2Screenshots()
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
                            ((MainViewModel)window.DataContext).ShowAdvanced = true;
                            var advanced = new AdvancedViewModel { Maintenance = new NoOpMaintenance() };
                            ((FrameworkElement)window.FindName("ViewAdvanced")).DataContext = advanced;
                            advanced.SetDevices(new[]
                            {
                                new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = "TX2_DUAL LINK_SFPA", IsTransmitter = true },
                                new AvasDevice { DeviceId = "74fe488b07bc", DeviceName = "TX2_DUAL LINK_SFPB", IsTransmitter = true, ChipId = 1 },
                                new AvasDevice { DeviceId = "0016c0589c1e", DeviceName = "VEGA camera", IsTransmitter = true, VendorId = 7 },
                                new AvasDevice { DeviceId = "74fe483c059e", DeviceName = "RX1_1080P_SFPA", IsReceiver = true },
                            });
                            advanced.Rows[0].ApplyTemperature(58);
                            advanced.Rows[0].ApplyLinkMode(new LinkModeInfo(LinkMode.Dual, "74fe488b07bc", "SINGLE,DUAL"));
                            advanced.Rows[1].ApplyTemperature(64);
                            advanced.Rows[3].ApplyTemperature(49);
                            ((RadioButton)window.FindName("TabAdvanced")).IsChecked = true;
                            Relayout(window);
                            SavePng(ClientRoot(window), Path.Combine(folder, $"01-advanced-danger-zone-{suffix}.png"));
                        }
                        finally { window.Close(); }

                        void Dialog(string name, DangerRequest request, string? typed = null)
                        {
                            var dialog = new DangerDialog(request) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = 0 };
                            dialog.Show();
                            try
                            {
                                themeService.SetTheme(mode);
                                if (typed != null) ((TextBox)dialog.FindName("TypedNameBox")).Text = typed;
                                Relayout(dialog);
                                SavePng(ClientRoot(dialog), Path.Combine(folder, $"{name}-{suffix}.png"));
                            }
                            finally { dialog.Close(); }
                        }

                        Dialog("02-reboot-chip-dialog", new DangerRequest(DangerAction.RebootChip, "TX2_DUAL LINK_SFPA", "Reboot TX2_DUAL LINK_SFPA?",
                            "TX2_DUAL LINK_SFPA restarts and its picture is lost for a while. Routes to it are not changed.", "Reboot", null));
                        var factory = new DangerRequest(DangerAction.FactoryResetChip, "TX2_DUAL LINK_SFPA", "Factory reset the chip of TX2_DUAL LINK_SFPA?",
                            "The video chip of TX2_DUAL LINK_SFPA goes back to its factory settings and restarts. Its name, mode and routes are lost and cannot be recovered.", "Factory reset", "TX2_DUAL LINK_SFPA");
                        Dialog("03-factory-reset-typed-empty", factory);
                        Dialog("04-factory-reset-typed-match", factory, "TX2_DUAL LINK_SFPA");
                    }
                }
                finally { themeService.SetTheme(ThemeMode.Dark); }
            });
        }
    }
}
