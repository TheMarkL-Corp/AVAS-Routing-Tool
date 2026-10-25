using System.Linq;
using System.Threading.Tasks;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class MainViewModelMultiViewTests
    {
        private sealed class Rig
        {
            public MockSdvoeClient Client { get; } = new();
            public MultiViewOrchestrator MultiView { get; }
            public MainViewModel Vm { get; }
            public AvasDevice Rx { get; } = new() { DeviceId = "rx1", DeviceName = "RX1", IsReceiver = true, IsActive = true };
            public DisplayCardViewModel RxVm { get; }
            public AvasDevice Tx1 { get; } = new() { DeviceId = "tx1", DeviceName = "TX1", VendorId = 105, IsTransmitter = true, IsActive = true };
            public AvasDevice Tx2 { get; } = new() { DeviceId = "tx2", DeviceName = "TX2", VendorId = 105, IsTransmitter = true, IsActive = true };
            public SourceCardViewModel Tx1Vm { get; }
            public SourceCardViewModel Tx2Vm { get; }

            public Rig()
            {
                MultiView = new MultiViewOrchestrator(Client);
                Vm = new MainViewModel(new ConfigService(), Client, new MockRtpReceiver(), new MulticastIpManager(), new RoutingOrchestrator(Client), MultiView);
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
            Assert.False(rig.RxVm.IsMultiView);
            Assert.Empty(rig.Client.MultiViewCalls); // nothing was taken, so the decoder is still in Single View
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
        public async Task AssignSlot_TheSameSourceInASecondEqualBlock_IsAddedNotMoved()
        {
            var rig = new Rig();
            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);
            await rig.Vm.AssignSlotAsync(rig.Rx, 0, rig.Tx1);

            bool ok = await rig.Vm.AssignSlotAsync(rig.Rx, 3, rig.Tx1);

            Assert.True(ok);
            Assert.Equal("TX1", rig.RxVm.Slots.Single(s => s.Index == 0).SourceName);
            Assert.Equal("TX1", rig.RxVm.Slots.Single(s => s.Index == 3).SourceName);
            Assert.Single(rig.Client.LastSubscriptions!); // one encoder, one subscription read by both windows
        }

        [Fact]
        public async Task AssignSlot_TheSameSourceInABlockOfAnotherSize_IsRefusedWithoutHardwareCalls()
        {
            var rig = new Rig();
            await rig.Vm.ApplyLayoutAsync("quad_1_3", rig.Rx);
            await rig.Vm.AssignSlotAsync(rig.Rx, 0, rig.Tx1); // the big focus block
            rig.Client.MultiViewCalls.Clear();

            bool ok = await rig.Vm.AssignSlotAsync(rig.Rx, 1, rig.Tx1);

            Assert.False(ok);
            Assert.Contains("One encoder has one size per layout", rig.RxVm.LastError);
            Assert.False(rig.RxVm.Slots.Single(s => s.Index == 1).IsAssigned);
            Assert.Empty(rig.Client.MultiViewCalls);
        }

        [Fact]
        public async Task AssignSlot_OfflineOrNonTransmitterSource_IsRefusedWithoutHardwareCalls()
        {
            var rig = new Rig();
            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);
            rig.Tx1.IsActive = false;
            var notTx = new AvasDevice { DeviceId = "rx9", DeviceName = "RX9", IsReceiver = true, IsActive = true };

            bool offline = await rig.Vm.AssignSlotAsync(rig.Rx, 0, rig.Tx1);
            Assert.Contains("TX1 is offline", rig.RxVm.LastError);
            bool wrongKind = await rig.Vm.AssignSlotAsync(rig.Rx, 0, notTx);

            Assert.False(offline);
            Assert.False(wrongKind);
            Assert.Contains("not a transmitter", rig.RxVm.LastError);
            Assert.Empty(rig.Client.MultiViewCalls);
        }

        [Fact]
        public async Task AssignSlot_OnADisplayWithoutALayout_IsRefused()
        {
            var rig = new Rig();

            Assert.False(await rig.Vm.AssignSlotAsync(rig.Rx, 0, rig.Tx1));
            Assert.Empty(rig.Client.MultiViewCalls);
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

        // ---- drops and Off on one display are handled one at a time ---------------------------------

        /// <summary>
        /// Holds the first multiview call starting with <paramref name="prefix"/> until <c>release</c> is called;
        /// <c>entered</c> completes once that call is in flight.
        /// </summary>
        private static (Task Entered, System.Action Release) HoldFirstCall(MockSdvoeClient client, string prefix)
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.DuringCall = entry =>
                entry.StartsWith(prefix) && entered.TrySetResult() ? release.Task : Task.CompletedTask;
            return (entered.Task, () => release.TrySetResult());
        }

        [Fact]
        public async Task TwoQuickDrops_OfDifferentSources_BothEndUpLiveAndOnTheCard()
        {
            var rig = new Rig();
            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);
            var (entered, release) = HoldFirstCall(rig.Client, "multiview:");

            var first = rig.Vm.AssignSlotAsync(rig.Rx, 0, rig.Tx1);
            await entered; // the first take is in flight
            var second = rig.Vm.AssignSlotAsync(rig.Rx, 1, rig.Tx2);
            release();
            var results = await Task.WhenAll(first, second);

            Assert.All(results, Assert.True);
            var live = rig.MultiView.GetLive(rig.Rx)!;
            Assert.Equal(new[] { (0, "tx1"), (1, "tx2") }, live.Placements.OrderBy(p => p.Key).Select(p => (p.Key, p.Value.SourceMac)));
            Assert.Equal("TX1", rig.RxVm.Slots.Single(s => s.Index == 0).SourceName);
            Assert.Equal("TX2", rig.RxVm.Slots.Single(s => s.Index == 1).SourceName);
        }

        [Fact]
        public async Task OffIssuedBeforeADrop_WinsAndTheDropFindsNoLayout()
        {
            var rig = new Rig();
            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);
            await rig.Vm.AssignSlotAsync(rig.Rx, 0, rig.Tx1);
            var (entered, release) = HoldFirstCall(rig.Client, "genlock:");

            var off = rig.Vm.UnrouteVideoAsync(rig.Rx);
            await entered; // Off is in flight
            var drop = rig.Vm.AssignSlotAsync(rig.Rx, 1, rig.Tx2);
            release();
            await Task.WhenAll(off, drop);

            Assert.True(off.Result);
            Assert.False(drop.Result);
            Assert.False(rig.MultiView.IsMultiView(rig.Rx)); // the decoder stayed off...
            Assert.False(rig.RxVm.IsMultiView);              // ...and so does the card
            Assert.Equal("genlock:rx1", rig.Client.MultiViewCalls.Last());
        }

        [Fact]
        public async Task ADropIssuedBeforeOff_IsTakenThenTurnedOff()
        {
            var rig = new Rig();
            await rig.Vm.ApplyLayoutAsync("matrix2x2", rig.Rx);
            await rig.Vm.AssignSlotAsync(rig.Rx, 0, rig.Tx1);
            var (entered, release) = HoldFirstCall(rig.Client, "multiview:");

            var drop = rig.Vm.AssignSlotAsync(rig.Rx, 1, rig.Tx2);
            await entered; // the drop's take is in flight
            var off = rig.Vm.UnrouteVideoAsync(rig.Rx);
            release();
            await Task.WhenAll(drop, off);

            Assert.True(drop.Result);
            Assert.True(off.Result);
            Assert.False(rig.MultiView.IsMultiView(rig.Rx));
            Assert.False(rig.RxVm.IsMultiView);
            Assert.Contains("rx1:2", rig.Client.Leaves); // Off left the dropped source's subscription too
            Assert.Equal("genlock:rx1", rig.Client.MultiViewCalls.Last());
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
