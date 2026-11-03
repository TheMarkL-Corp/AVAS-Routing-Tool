using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class AdvancedLinkSwitchTests
    {
        private sealed class FakeSwitcher : ILinkSwitcher
        {
            public List<(string Mac, LinkMode Target)> Calls { get; } = new();
            public bool Result { get; set; } = true;
            public Task<bool> SetLinkModeAsync(AvasDevice tx, LinkMode target)
            {
                Calls.Add((tx.DeviceId, target));
                return Task.FromResult(Result);
            }
        }

        private sealed class Rig
        {
            public AdvancedViewModel Vm { get; } = new();
            public FakeSwitcher Switcher { get; } = new();
            public List<string> Order { get; } = new();
            public List<IReadOnlyList<string>> Unrouted { get; } = new();
            public DateTime Now { get; set; } = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
            public LinkSwitchImpact Impact { get; set; } = LinkSwitchImpact.None;
            public LinkSwitchRequest? Asked { get; private set; }
            public bool Answer { get; set; } = true;

            public Rig()
            {
                Vm.Switcher = Switcher;
                Vm.Clock = () => Now;
                Vm.ImpactProvider = macs => Impact;
                Vm.UnrouteFromMultiViews = macs => { Order.Add("unroute"); Unrouted.Add(macs); return Task.CompletedTask; };
                Vm.Confirm = req => { Asked = req; Order.Add("confirm"); return Task.FromResult(Answer); };
            }

            public AvasDevice Tx { get; } = new() { DeviceId = "74fe488b07bb", DeviceName = "TX2", IsTransmitter = true, ChipId = 0 };
            public AvasDevice Companion { get; } = new() { DeviceId = "74fe488b07bc", DeviceName = "TX2 (chip 1)", IsTransmitter = true, ChipId = 1 };

            public AdvancedDeviceRowViewModel Row(LinkMode mode, string companion = "NONE")
            {
                Vm.SetDevices(new[] { Tx, Companion });
                var row = Vm.Rows.Single(r => r.Mac == Tx.DeviceId);
                row.ApplyLinkMode(new LinkModeInfo(mode, companion, "SINGLE,DUAL"));
                return row;
            }
        }

        [Fact]
        public async Task Cancel_ChangesNothing()
        {
            var r = new Rig { Answer = false };
            var row = r.Row(LinkMode.Single);

            await r.Vm.SwitchLinkAsync(row);

            Assert.Empty(r.Switcher.Calls);
            Assert.Empty(r.Unrouted);
            Assert.Equal("Single link", row.LinkMode);
            Assert.False(row.IsRebooting);
        }

        [Fact]
        public async Task Confirm_SwitchesToTheOtherMode_ThenUnroutesMultiViews_AndShowsRebooting()
        {
            var r = new Rig();
            var row = r.Row(LinkMode.Single);

            await r.Vm.SwitchLinkAsync(row);

            Assert.Equal(("74fe488b07bb", LinkMode.Dual), Assert.Single(r.Switcher.Calls));
            Assert.Equal(new[] { "confirm", "unroute" }, r.Order);
            Assert.Equal("TX2", r.Asked!.DeviceName);
            Assert.Equal(LinkMode.Dual, r.Asked.Target);
            Assert.True(row.IsRebooting);
            Assert.Equal("Rebooting", row.LinkMode);
        }

        [Fact]
        public async Task Unroute_CoversThePrimaryAndTheCompanionChip_WhenDual()
        {
            var r = new Rig();
            var row = r.Row(LinkMode.Dual, companion: "74fe488b07bc");

            await r.Vm.SwitchLinkAsync(row);

            Assert.Equal(LinkMode.Single, Assert.Single(r.Switcher.Calls).Target);
            Assert.Equal(new[] { "74fe488b07bb", "74fe488b07bc" }, Assert.Single(r.Unrouted).OrderBy(m => m).ToArray());
        }

        [Fact]
        public async Task ARefusedSwitch_UnroutesNothing_ShowsABanner_AndKeepsTheOldMode()
        {
            var r = new Rig();
            r.Switcher.Result = false;
            var row = r.Row(LinkMode.Single);

            await r.Vm.SwitchLinkAsync(row);

            Assert.Empty(r.Unrouted);
            Assert.False(row.IsRebooting);
            Assert.Equal("Single link", row.LinkMode);
            Assert.Contains("TX2", r.Vm.Banner);
        }

        [Fact]
        public void TheCompanionChipRow_AndUnknownModes_CannotSwitch()
        {
            var r = new Rig();
            var row = r.Row(LinkMode.Single);
            var companionRow = r.Vm.Rows.Single(x => x.Mac == r.Companion.DeviceId);
            companionRow.ApplyLinkMode(new LinkModeInfo(LinkMode.Single, "NONE", "SINGLE,DUAL"));

            Assert.True(row.CanSwitchLink);
            Assert.False(companionRow.CanSwitchLink);
            row.ApplyLinkMode(LinkModeInfo.Unknown);
            Assert.False(row.CanSwitchLink);
        }

        [Fact]
        public async Task Rebooting_StaysUntilTheDeviceLeavesAndComesBack()
        {
            var r = new Rig();
            var row = r.Row(LinkMode.Single);
            await r.Vm.SwitchLinkAsync(row);

            r.Vm.SetDevices(new[] { r.Tx, r.Companion }); // still listed: not rediscovered yet
            Assert.True(r.Vm.Rows.Single(x => x.Mac == r.Tx.DeviceId).IsRebooting);

            r.Vm.SetDevices(new[] { r.Companion });       // gone
            r.Vm.SetDevices(new[] { r.Tx, r.Companion }); // back
            Assert.False(r.Vm.Rows.Single(x => x.Mac == r.Tx.DeviceId).IsRebooting);
        }

        [Fact]
        public async Task Rebooting_GivesUpAfterTheTimeout()
        {
            var r = new Rig();
            var row = r.Row(LinkMode.Single);
            await r.Vm.SwitchLinkAsync(row);

            r.Now += AdvancedViewModel.RebootTimeout + TimeSpan.FromSeconds(1);
            r.Vm.SetDevices(new[] { r.Tx, r.Companion });

            Assert.False(r.Vm.Rows.Single(x => x.Mac == r.Tx.DeviceId).IsRebooting);
        }
    }

    public class LinkSwitchMainViewModelTests
    {
        private sealed class Rig
        {
            public MockSdvoeClient Client { get; } = new();
            public MultiViewOrchestrator MultiView { get; }
            public MainViewModel Vm { get; }
            public AvasDevice Tx1 { get; } = new() { DeviceId = "tx1", DeviceName = "TX1", VendorId = 105, IsTransmitter = true, IsActive = true };
            public AvasDevice Rx1 { get; } = new() { DeviceId = "rx1", DeviceName = "RX1", IsReceiver = true, IsActive = true };
            public AvasDevice Rx2 { get; } = new() { DeviceId = "rx2", DeviceName = "RX2", IsReceiver = true, IsActive = true };
            public DisplayCardViewModel Rx1Vm { get; }
            public DisplayCardViewModel Rx2Vm { get; }
            public SourceCardViewModel Tx1Vm { get; }

            public Rig()
            {
                MultiView = new MultiViewOrchestrator(Client);
                Vm = new MainViewModel(new AvasRoutingTool.Configuration.ConfigService(), Client, new MockRtpReceiver(), new MulticastIpManager(), new RoutingOrchestrator(Client), MultiView);
                Rx1Vm = new DisplayCardViewModel(Rx1, Vm.UnrouteVideoAsync);
                Rx2Vm = new DisplayCardViewModel(Rx2, Vm.UnrouteVideoAsync);
                Tx1Vm = new SourceCardViewModel(Tx1);
                Vm.Displays.Add(Rx1Vm);
                Vm.Displays.Add(Rx2Vm);
                Vm.Sources.Add(Tx1Vm);
            }

            /// <summary>RX1 shows TX1 in block 0 of a 2x2 multi-view; RX2 has a single-view route from TX1.</summary>
            public async Task SetUpAsync()
            {
                await Vm.ChooseLayoutFromCardAsync(Rx1Vm, "matrix2x2");
                Vm.ActiveComposer!.Arm(Tx1Vm);
                Vm.ActiveComposer.TapBlock(0);
                Vm.ActiveComposer.Disarm();
                await Vm.ActiveComposer.TakeAsync();
                Vm.CloseComposer();
                Assert.True(await Vm.RouteVideoAsync(Tx1, Rx2));
                Assert.True(MultiView.IsMultiView(Rx1));
            }
        }

        [Fact]
        public async Task Impact_ListsTheSingleViewRoute_AndTheMultiViewLayout_UsingTheTx()
        {
            var rig = new Rig();
            await rig.SetUpAsync();

            var impact = rig.Vm.GetLinkSwitchImpact(new[] { "tx1" });

            Assert.Equal(new[] { "RX2" }, impact.SingleViewDisplays);
            Assert.Equal(new[] { "RX1" }, impact.MultiViewDisplays);
            Assert.True(impact.HasAny);
        }

        [Fact]
        public async Task UnroutingTheTx_ClearsItsMultiViewBlocks_ButKeepsTheSingleViewRoute()
        {
            var rig = new Rig();
            await rig.SetUpAsync();

            await rig.Vm.UnrouteSourceFromMultiViewsAsync(new[] { "tx1" });

            Assert.False(rig.MultiView.IsMultiView(rig.Rx1)); // its only block was the TX: the display is off
            var after = rig.Vm.GetLinkSwitchImpact(new[] { "tx1" });
            Assert.Empty(after.MultiViewDisplays);
            Assert.Equal(new[] { "RX2" }, after.SingleViewDisplays); // the single-view route stays
        }
    }

    [Collection("WpfSta")]
    public class LinkSwitchDialogTests
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
            if (failure != null) throw new Xunit.Sdk.XunitException("Link switch dialog failure: " + failure);
        }

        [Fact]
        public void Dialog_NamesTheDevice_ListsWhatIsAffected_AndOffersUnrouteAndSwitchOrCancel() => RunSta(() =>
        {
            var impact = new LinkSwitchImpact(new[] { "Room RX" }, new[] { "Wall RX" });
            var d = new LinkSwitchDialog(new LinkSwitchRequest("TX2", LinkMode.Dual, impact));
            try
            {
                Assert.Contains("TX2", d.Title);
                var text = ((TextBlock)d.FindName("AffectedText")).Text;
                Assert.Contains("Room RX", text);
                Assert.Contains("Wall RX", text);
                Assert.Equal("Unroute and switch", ((Button)d.FindName("ConfirmButton")).Content);
                Assert.Equal("Cancel", ((Button)d.FindName("CancelButton")).Content);
                Assert.True(((Button)d.FindName("CancelButton")).IsCancel);
                Assert.False(((Button)d.FindName("ConfirmButton")).IsDefault); // the destructive one is never the default
            }
            finally { d.Close(); }
        });

        [Fact]
        public void Dialog_WhenNothingIsRouted_OffersPlainSwitch() => RunSta(() =>
        {
            var d = new LinkSwitchDialog(new LinkSwitchRequest("TX2", LinkMode.Single, LinkSwitchImpact.None));
            try
            {
                Assert.Equal("Switch", ((Button)d.FindName("ConfirmButton")).Content);
                Assert.Contains("Nothing is routed", ((TextBlock)d.FindName("AffectedText")).Text);
            }
            finally { d.Close(); }
        });
    }
}
