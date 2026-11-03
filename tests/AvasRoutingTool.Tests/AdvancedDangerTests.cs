using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>Stage 2: the destructive actions (D-A2, D-A4). Everything here runs against fakes; no command reaches a device.</summary>
    public class AdvancedDangerTests
    {
        private sealed class FakeMaintenance : IMaintenanceActions
        {
            public List<string> Calls { get; } = new();
            public bool Result { get; set; } = true;

            public Task<bool> RebootChipAsync(AvasDevice device, string mac)
            {
                Calls.Add($"reboot-chip {mac}");
                return Task.FromResult(Result);
            }

            public Task<bool> RebootMcuAsync(AvasDevice device)
            {
                Calls.Add($"reboot-mcu {device.DeviceId}");
                return Task.FromResult(Result);
            }

            public Task<bool> FactoryResetChipAsync(AvasDevice device, string mac)
            {
                Calls.Add($"factory-chip {mac}");
                return Task.FromResult(Result);
            }

            public Task<bool> FactoryResetMcuAsync(AvasDevice device)
            {
                Calls.Add($"factory-mcu {device.DeviceId}");
                return Task.FromResult(Result);
            }
        }

        private sealed class Rig
        {
            public AdvancedViewModel Vm { get; } = new();
            public FakeMaintenance Maintenance { get; } = new();
            public DangerRequest? Asked { get; private set; }
            public bool Answer { get; set; } = true;
            public AvasDevice Tx { get; } = new() { DeviceId = "74fe488b07bb", DeviceName = "TX2", IsTransmitter = true, ChipId = 0 };
            public AvasDevice Companion { get; } = new() { DeviceId = "74fe488b07bc", DeviceName = "TX2 chip 1", IsTransmitter = true, ChipId = 1 };
            public AvasDevice Vega { get; } = new() { DeviceId = "0016c0589c1e", DeviceName = "VEGA", IsTransmitter = true, VendorId = 7 };
            public AvasDevice Rx { get; } = new() { DeviceId = "74fe483c059e", DeviceName = "RX1", IsReceiver = true };

            public Rig()
            {
                Vm.Maintenance = Maintenance;
                Vm.ConfirmDanger = req => { Asked = req; return Task.FromResult(Answer); };
                Vm.SetDevices(new[] { Tx, Companion, Vega, Rx });
            }

            public AdvancedDeviceRowViewModel Row(string mac) => Vm.Rows.Single(r => r.Mac == mac);
        }

        [Fact]
        public async Task RebootChip_AsksWithATitleNamingTheDevice_ThenRebootsThatChip()
        {
            var r = new Rig();

            await r.Vm.RunDangerAsync(DangerAction.RebootChip, r.Row("74fe488b07bb"));

            Assert.Equal("Reboot TX2?", r.Asked!.Title);
            Assert.Null(r.Asked.TypedName); // a plain reboot needs no typed confirmation
            Assert.Equal(new[] { "reboot-chip 74fe488b07bb" }, r.Maintenance.Calls);
            Assert.True(r.Row("74fe488b07bb").IsRebooting);
            Assert.Contains("TX2", r.Vm.Banner);
        }

        [Fact]
        public async Task RebootChip_Cancel_ChangesNothing()
        {
            var r = new Rig { Answer = false };

            await r.Vm.RunDangerAsync(DangerAction.RebootChip, r.Row("74fe488b07bb"));

            Assert.Empty(r.Maintenance.Calls);
            Assert.False(r.Row("74fe488b07bb").IsRebooting);
        }

        [Fact]
        public async Task RebootChip_Failure_ShowsABannerNamingTheDevice_AndNoRebootingState()
        {
            var r = new Rig();
            r.Maintenance.Result = false;

            await r.Vm.RunDangerAsync(DangerAction.RebootChip, r.Row("74fe488b07bb"));

            Assert.Contains("Couldn't reboot TX2", r.Vm.Banner);
            Assert.False(r.Row("74fe488b07bb").IsRebooting);
        }

        [Fact]
        public async Task RebootChip_WorksOnTheCompanionChipAndOnAnAvasReceiver()
        {
            var r = new Rig();

            await r.Vm.RunDangerAsync(DangerAction.RebootChip, r.Row("74fe488b07bc"));
            await r.Vm.RunDangerAsync(DangerAction.RebootChip, r.Row("74fe483c059e"));

            Assert.Equal(new[] { "reboot-chip 74fe488b07bc", "reboot-chip 74fe483c059e" }, r.Maintenance.Calls);
        }

        // ---- A7: MCU reboot (through chip 0) ----

        [Fact]
        public async Task RebootMcu_AsksWithATitleNamingTheDevice_ThenRebootsTheMcuOfChip0()
        {
            var r = new Rig();

            await r.Vm.RunDangerAsync(DangerAction.RebootMcu, r.Row("74fe488b07bb"));

            Assert.Equal("Reboot the MCU of TX2?", r.Asked!.Title);
            Assert.Null(r.Asked.TypedName);
            Assert.Equal(new[] { "reboot-mcu 74fe488b07bb" }, r.Maintenance.Calls);
            Assert.False(r.Row("74fe488b07bb").IsRebooting); // only the MCU restarts, the chip keeps running
            Assert.Contains("TX2", r.Vm.Banner);
        }

        [Fact]
        public async Task RebootMcu_FromTheCompanionChipRow_IsRefused_BecauseMcuCommandsGoThroughChip0()
        {
            var r = new Rig();
            var companion = r.Row("74fe488b07bc");

            Assert.False(companion.CanRunMcuActions);
            await r.Vm.RunDangerAsync(DangerAction.RebootMcu, companion);

            Assert.Null(r.Asked);
            Assert.Empty(r.Maintenance.Calls);
            Assert.Contains("chip 0", r.Vm.Banner);
        }

        [Fact]
        public async Task RebootMcu_Failure_ShowsABanner()
        {
            var r = new Rig();
            r.Maintenance.Result = false;

            await r.Vm.RunDangerAsync(DangerAction.RebootMcu, r.Row("74fe488b07bb"));

            Assert.Contains("Couldn't reboot the MCU of TX2", r.Vm.Banner);
        }

        // ---- A8: MCU factory reset (typed device name) ----

        [Fact]
        public async Task FactoryResetMcu_AsksForTheTypedDeviceName_AndResetsTheMcuOfChip0()
        {
            var r = new Rig();

            await r.Vm.RunDangerAsync(DangerAction.FactoryResetMcu, r.Row("74fe488b07bb"));

            Assert.Equal("Factory reset the MCU of TX2?", r.Asked!.Title);
            Assert.Equal("TX2", r.Asked.TypedName);
            Assert.Equal(new[] { "factory-mcu 74fe488b07bb" }, r.Maintenance.Calls);
            Assert.Contains("TX2", r.Vm.Banner);
        }

        [Fact]
        public async Task FactoryResetMcu_Cancel_ChangesNothing()
        {
            var r = new Rig { Answer = false };

            await r.Vm.RunDangerAsync(DangerAction.FactoryResetMcu, r.Row("74fe488b07bb"));

            Assert.Empty(r.Maintenance.Calls);
        }

        [Fact]
        public async Task FactoryResetMcu_FromTheCompanionChip_AndOnNonAvas_IsRefused()
        {
            var r = new Rig();

            await r.Vm.RunDangerAsync(DangerAction.FactoryResetMcu, r.Row("74fe488b07bc"));
            await r.Vm.RunDangerAsync(DangerAction.FactoryResetMcu, r.Row("0016c0589c1e"));

            Assert.Null(r.Asked);
            Assert.Empty(r.Maintenance.Calls);
        }

        [Fact]
        public async Task FactoryResetMcu_Failure_ShowsABanner()
        {
            var r = new Rig();
            r.Maintenance.Result = false;

            await r.Vm.RunDangerAsync(DangerAction.FactoryResetMcu, r.Row("74fe488b07bb"));

            Assert.Contains("Couldn't factory reset the MCU of TX2", r.Vm.Banner);
        }

        // ---- A9: chip factory reset (typed device name) ----

        [Fact]
        public async Task FactoryResetChip_AsksForTheTypedDeviceName_ThenResetsThatChip()
        {
            var r = new Rig();

            await r.Vm.RunDangerAsync(DangerAction.FactoryResetChip, r.Row("74fe488b07bb"));

            Assert.Equal("Factory reset the chip of TX2?", r.Asked!.Title);
            Assert.Equal("TX2", r.Asked.TypedName);
            Assert.Equal(new[] { "factory-chip 74fe488b07bb" }, r.Maintenance.Calls);
            Assert.True(r.Row("74fe488b07bb").IsRebooting); // a reset chip restarts
            Assert.Contains("TX2", r.Vm.Banner);
        }

        [Fact]
        public async Task FactoryResetChip_Cancel_ChangesNothing()
        {
            var r = new Rig { Answer = false };

            await r.Vm.RunDangerAsync(DangerAction.FactoryResetChip, r.Row("74fe488b07bb"));

            Assert.Empty(r.Maintenance.Calls);
            Assert.False(r.Row("74fe488b07bb").IsRebooting);
        }

        [Fact]
        public async Task FactoryResetChip_OnTheCompanionChip_TargetsThatChip_AndNonAvasIsRefused()
        {
            var r = new Rig();

            await r.Vm.RunDangerAsync(DangerAction.FactoryResetChip, r.Row("74fe488b07bc"));
            await r.Vm.RunDangerAsync(DangerAction.FactoryResetChip, r.Row("0016c0589c1e"));

            Assert.Equal(new[] { "factory-chip 74fe488b07bc" }, r.Maintenance.Calls);
        }

        [Fact]
        public async Task FactoryResetChip_Failure_ShowsABanner_AndNoRebootingState()
        {
            var r = new Rig();
            r.Maintenance.Result = false;

            await r.Vm.RunDangerAsync(DangerAction.FactoryResetChip, r.Row("74fe488b07bb"));

            Assert.Contains("Couldn't factory reset the chip of TX2", r.Vm.Banner);
            Assert.False(r.Row("74fe488b07bb").IsRebooting);
        }

        [Fact]
        public async Task NonAvasDevice_IsRefused_WithoutAskingOrCalling()
        {
            var r = new Rig();
            var vega = r.Row("0016c0589c1e");

            Assert.False(vega.CanRunDangerActions);
            await r.Vm.RunDangerAsync(DangerAction.RebootChip, vega);

            Assert.Null(r.Asked);
            Assert.Empty(r.Maintenance.Calls);
            Assert.Contains("AVAS", r.Vm.Banner);
        }
    }

    [Collection("WpfSta")]
    public class DangerDialogTests
    {
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
            if (failure != null) throw new Xunit.Sdk.XunitException("Danger dialog failure: " + failure);
        }

        [Fact]
        public void Dialog_TitleNamesTheDevice_ConfirmIsNeverPrimary_CancelIsTheEscape() => RunSta(() =>
        {
            var d = new DangerDialog(new DangerRequest(DangerAction.RebootChip, "TX2", "Reboot TX2?", "It drops its picture while it restarts.", "Reboot", null));
            try
            {
                Assert.Equal("Reboot TX2?", d.Title);
                var confirm = (Button)d.FindName("ConfirmButton");
                var cancel = (Button)d.FindName("CancelButton");
                Assert.Equal("Reboot", confirm.Content);
                Assert.True(confirm.IsEnabled);
                Assert.NotSame(d.FindResource("Style.Button.Primary"), confirm.Style); // a destructive action is never the primary button
                Assert.True(cancel.IsCancel);
                Assert.False(confirm.IsDefault);
            }
            finally { d.Close(); }
        });

        [Fact]
        public void TypedConfirm_StaysDisabledUntilTheNameMatchesExactly() => RunSta(() =>
        {
            var d = new DangerDialog(new DangerRequest(DangerAction.FactoryResetMcu, "TX2", "Factory reset the MCU of TX2?", "Settings are lost.", "Factory reset", "TX2"));
            try
            {
                var confirm = (Button)d.FindName("ConfirmButton");
                var typed = (TextBox)d.FindName("TypedNameBox");
                Assert.Contains("TX2", ((TextBlock)d.FindName("TypedPrompt")).Text);
                Assert.False(confirm.IsEnabled);

                foreach (var wrong in new[] { "", "tx2", "TX2 ", " TX2", "TX", "TX22" })
                {
                    typed.Text = wrong;
                    Assert.False(confirm.IsEnabled, $"'{wrong}' must not enable the button");
                }

                typed.Text = "TX2";
                Assert.True(confirm.IsEnabled);
                Assert.NotSame(d.FindResource("Style.Button.Primary"), confirm.Style);
            }
            finally { d.Close(); }
        });

        [Fact]
        public void WithoutATypedName_NoTypedFieldIsShown() => RunSta(() =>
        {
            var d = new DangerDialog(new DangerRequest(DangerAction.RebootChip, "TX2", "Reboot TX2?", "Body.", "Reboot", null));
            try
            {
                Assert.Equal(System.Windows.Visibility.Collapsed, ((TextBox)d.FindName("TypedNameBox")).Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed, ((TextBlock)d.FindName("TypedPrompt")).Visibility);
            }
            finally { d.Close(); }
        });
    }
}
