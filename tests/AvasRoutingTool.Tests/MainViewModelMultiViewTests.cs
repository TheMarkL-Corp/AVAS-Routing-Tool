using System.Linq;
using System.Threading.Tasks;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class MainViewModelMultiViewTests
    {
        private sealed class Rig
        {
            public MockSdvoeClient Client { get; } = new();
            public MainViewModel Vm { get; }
            public AvasDevice Rx { get; } = new() { DeviceId = "rx1", DeviceName = "RX1", IsReceiver = true, IsActive = true };
            public DisplayCardViewModel RxVm { get; }
            public AvasDevice Tx1 { get; } = new() { DeviceId = "tx1", DeviceName = "TX1", VendorId = 105, IsTransmitter = true, IsActive = true };
            public AvasDevice Tx2 { get; } = new() { DeviceId = "tx2", DeviceName = "TX2", VendorId = 105, IsTransmitter = true, IsActive = true };
            public SourceCardViewModel Tx1Vm { get; }
            public SourceCardViewModel Tx2Vm { get; }

            public Rig()
            {
                Vm = new MainViewModel(new ConfigService(), Client, new MockRtpReceiver(), new MulticastIpManager(), new RoutingOrchestrator(Client));
                RxVm = new DisplayCardViewModel(Rx, Vm.UnrouteVideoAsync);
                Tx1Vm = new SourceCardViewModel(Tx1);
                Tx2Vm = new SourceCardViewModel(Tx2);
                Vm.Displays.Add(RxVm);
                Vm.Sources.Add(Tx1Vm);
                Vm.Sources.Add(Tx2Vm);
            }
        }

        [Fact]
        public void Layouts_AllFourMultiViewPresetsAreAvailable()
        {
            var rig = new Rig();

            foreach (var id in new[] { "matrix2x2", "pip", "side_by_side", "quad_1_3" })
            {
                var layout = rig.Vm.Layouts.Single(l => l.Id == id);
                Assert.True(layout.IsAvailable, id);
                Assert.Equal("READY", layout.BadgeText);
            }
        }

        [Fact]
        public async Task ApplyLayout_OnUnroutedDisplay_ShowsEmptyWindowsWithoutHardwareCalls()
        {
            var rig = new Rig();

            bool ok = await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);

            Assert.True(ok);
            Assert.True(rig.RxVm.IsMultiView);
            Assert.Equal(4, rig.RxVm.Slots.Count);
            Assert.Empty(rig.Client.MultiViewCalls);
        }

        [Fact]
        public async Task ApplyLayout_On4kDisplay_UsesTheDisplayEdidAndShowsTheOutputFormat()
        {
            var rig = new Rig();
            rig.Client.DisplayEdid = TestEdids.ViewSonic4k;

            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);

            Assert.Contains("3840x2160@60", rig.RxVm.CanvasInfo);
            Assert.Contains("VP3268-4K", rig.RxVm.CanvasInfo);
            Assert.Contains("Gbps", rig.RxVm.CanvasInfo); // four 1080p60 windows exceed the 9 Gbps guideline
            Assert.Equal(16.0 / 9.0, rig.RxVm.CanvasAspect, 3);
        }

        [Fact]
        public async Task ApplyLayout_WithoutAnEdid_ShowsTheFallbackAndWhy()
        {
            var rig = new Rig();
            rig.Client.DisplayEdid = null;

            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);

            Assert.Contains("1920x1080@60", rig.RxVm.CanvasInfo);
            Assert.Contains("EDID unavailable", rig.RxVm.CanvasInfo);
        }

        [Fact]
        public async Task ApplyLayout_OnASixteenByTenDisplay_KeepsItsAspectForTheWindowSurface()
        {
            var rig = new Rig();
            rig.Client.DisplayEdid = TestEdids.Build((1920, 1200, 60, false));

            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);

            Assert.Equal(1920.0 / 1200.0, rig.RxVm.CanvasAspect, 3);
        }

        [Fact]
        public async Task AssignSlot_WhenTheDisplayWasSwappedMeanwhile_RedrawsTheCardForTheNewOutput()
        {
            var rig = new Rig();
            rig.Client.DisplayEdid = TestEdids.Build((1920, 1080, 60, false));
            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);
            Assert.Contains("1920x1080@60", rig.RxVm.CanvasInfo);

            rig.Client.DisplayEdid = TestEdids.ViewSonic4k;
            bool ok = await rig.Vm.AssignSlotAsync(rig.Rx, 1, rig.Tx1);

            Assert.True(ok);
            Assert.Contains("3840x2160@60", rig.RxVm.CanvasInfo);
            Assert.Equal(4, rig.RxVm.Slots.Count);
            Assert.True(rig.RxVm.Slots.Single(s => s.Index == 1).IsAssigned);
        }

        [Fact]
        public async Task ApplyLayoutSingle_ClearsTheOutputFormat()
        {
            var rig = new Rig();
            rig.Client.DisplayEdid = TestEdids.ViewSonic4k;
            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);

            await rig.Vm.ApplyLayoutAsync("single", rig.Rx);

            Assert.Empty(rig.RxVm.CanvasInfo);
        }

        [Fact]
        public async Task ApplyLayout_OnRoutedDisplay_KeepsTheCurrentSourceInTheFirstWindow()
        {
            var rig = new Rig();
            await rig.Vm.RouteVideoAsync(rig.Tx1, rig.Rx);

            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);

            var first = rig.RxVm.Slots.Single(s => s.Index == 0);
            Assert.True(first.IsAssigned);
            Assert.Equal("TX1", first.SourceName);
            Assert.Contains(rig.Client.MultiViewCalls, c => c.StartsWith("multiview:rx1:"));
            Assert.Empty(rig.Rx.ActiveSourceId);
        }

        [Fact]
        public async Task AssignSlot_Success_FillsTheWindow()
        {
            var rig = new Rig();
            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);

            bool ok = await rig.Vm.AssignSlotAsync(rig.Rx, 2, rig.Tx2);

            Assert.True(ok);
            var slot = rig.RxVm.Slots.Single(s => s.Index == 2);
            Assert.True(slot.IsAssigned);
            Assert.Equal("TX2", slot.SourceName);
            Assert.True(rig.RxVm.IsRouted);
            Assert.False(rig.RxVm.HasError);
        }

        [Fact]
        public async Task AssignSlot_Failure_LeavesWindowEmptyAndReportsTheError()
        {
            var rig = new Rig();
            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);
            rig.Client.FailMultiViewPrefix = "multiview:";

            bool ok = await rig.Vm.AssignSlotAsync(rig.Rx, 0, rig.Tx1);

            Assert.False(ok);
            Assert.False(rig.RxVm.Slots.Single(s => s.Index == 0).IsAssigned);
            Assert.True(rig.RxVm.HasError);
            Assert.Contains("RX1", rig.RxVm.LastError);
        }

        [Fact]
        public async Task AssignSlot_MovingASourceEmptiesItsOldWindow()
        {
            var rig = new Rig();
            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);
            await rig.Vm.AssignSlotAsync(rig.Rx, 0, rig.Tx1);

            await rig.Vm.AssignSlotAsync(rig.Rx, 3, rig.Tx1);

            Assert.False(rig.RxVm.Slots.Single(s => s.Index == 0).IsAssigned);
            Assert.True(rig.RxVm.Slots.Single(s => s.Index == 3).IsAssigned);
        }

        [Fact]
        public async Task AssignSlot_ClearsAPreviousErrorOnSuccess()
        {
            var rig = new Rig();
            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);
            rig.RxVm.LastError = "old";

            await rig.Vm.AssignSlotAsync(rig.Rx, 0, rig.Tx1);

            Assert.False(rig.RxVm.HasError);
        }

        [Fact]
        public async Task RouteVideo_OnMultiViewDisplay_ReturnsToGenlockThenJoins()
        {
            var rig = new Rig();
            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);
            await rig.Vm.AssignSlotAsync(rig.Rx, 0, rig.Tx1);
            rig.Client.MultiViewCalls.Clear();

            bool ok = await rig.Vm.RouteVideoAsync(rig.Tx2, rig.Rx);

            Assert.True(ok);
            Assert.Equal(new[] { "genlock:rx1" }, rig.Client.MultiViewCalls);
            Assert.Equal("tx2", rig.Client.LastVideoTx);
            Assert.False(rig.RxVm.IsMultiView);
            Assert.Empty(rig.RxVm.Slots);
            Assert.Equal("tx2", rig.RxVm.ActiveSourceId);
        }

        [Fact]
        public async Task Unroute_OnMultiViewDisplay_TurnsEverythingOff()
        {
            var rig = new Rig();
            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);
            await rig.Vm.AssignSlotAsync(rig.Rx, 0, rig.Tx1);

            bool ok = await rig.Vm.UnrouteVideoAsync(rig.Rx);

            Assert.True(ok);
            Assert.False(rig.RxVm.IsMultiView);
            Assert.False(rig.RxVm.IsRouted);
            Assert.Equal("rx1", rig.Client.LastUnroutedRx);
        }

        [Fact]
        public async Task ApplyLayoutSingle_OnMultiViewDisplay_ReturnsToSingleView()
        {
            var rig = new Rig();
            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);
            await rig.Vm.AssignSlotAsync(rig.Rx, 0, rig.Tx1);
            rig.Client.MultiViewCalls.Clear();

            bool ok = await rig.Vm.ApplyLayoutAsync("single", rig.Rx);

            Assert.True(ok);
            Assert.Equal(new[] { "genlock:rx1" }, rig.Client.MultiViewCalls);
            Assert.False(rig.RxVm.IsMultiView);
        }

        [Fact]
        public async Task ApplyLayout_SwitchingBetweenMultiViewPresets_StartsWithEmptyWindows()
        {
            var rig = new Rig();
            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);
            await rig.Vm.AssignSlotAsync(rig.Rx, 0, rig.Tx1);

            await rig.Vm.ApplyLayoutAsync("side_by_side", rig.Rx);

            Assert.Equal(2, rig.RxVm.Slots.Count);
            Assert.All(rig.RxVm.Slots, s => Assert.False(s.IsAssigned));
        }

        [Fact]
        public async Task ApplyLayout_OnOfflineDisplay_IsRefused()
        {
            var rig = new Rig();
            rig.RxVm.SetOnlineState(false);

            bool ok = await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);

            Assert.False(ok);
            Assert.False(rig.RxVm.IsMultiView);
        }

        [Fact]
        public async Task ApplyLayout_UnknownId_IsRefused()
        {
            var rig = new Rig();

            Assert.False(await rig.Vm.ApplyLayoutAsync("nope", rig.Rx));
            Assert.False(rig.RxVm.IsMultiView);
        }
    }
}
