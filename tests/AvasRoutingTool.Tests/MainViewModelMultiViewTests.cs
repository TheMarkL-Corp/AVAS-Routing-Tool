using System.Linq;
using System.Threading.Tasks;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using AvasRoutingTool.ViewModels;
using Xunit;

// NOTE: test names in this file must not contain the substring "Live" (the FullyQualifiedName!~Live filter skips them).
namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// MultiView from the overview: a layout is staged in the Composer (no device change) and goes on screen with
    /// Take; the card shows what is on screen. Migrated from the interim per-drop API (ApplyLayoutAsync / AssignSlotAsync).
    /// </summary>
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

            public MultiViewComposerViewModel Composer => Vm.ActiveComposer!;

            /// <summary>Opens the Composer on RX1 with the layout staged (the card's Layout menu / a layout tile drop).</summary>
            public async Task<MultiViewComposerViewModel> Compose(string layoutId)
            {
                Assert.True(await Vm.ChooseLayoutFromCardAsync(RxVm, layoutId));
                return Composer;
            }

            /// <summary>Places the source in the block (API window index) and stops placing.</summary>
            public void Place(int slot, SourceCardViewModel source)
            {
                Composer.Arm(source);
                Composer.TapBlock(slot);
                Composer.Disarm();
            }

            public async Task TakeMatrixWithTx1Async()
            {
                await Compose("matrix2x2");
                Place(0, Tx1Vm);
                await Composer.TakeAsync();
                Vm.CloseComposer();
                Client.MultiViewCalls.Clear();
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
        public async Task ChoosingAPreset_OnAnUnroutedDisplay_StagesEmptyBlocksWithoutHardwareCalls()
        {
            var rig = new Rig();

            var composer = await rig.Compose("matrix2x2");

            Assert.Equal(4, composer.Blocks.Count);
            Assert.All(composer.Blocks, b => Assert.False(b.IsPlaced));
            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.False(rig.RxVm.IsMultiView); // the card shows the screen, which did not change
        }

        [Fact]
        public async Task Take_On4kDisplay_UsesTheDisplayEdid_AndTheCardShowsTheOutputFormat()
        {
            var rig = new Rig();
            rig.Client.DisplayEdid = TestEdids.ViewSonic4k;
            var composer = await rig.Compose("matrix2x2");
            Assert.Equal("OUT 3840x2160@60 (VP3268-4K)", composer.OutputText);
            rig.Place(0, rig.Tx1Vm);

            await composer.TakeAsync();

            Assert.Contains("3840x2160@60", rig.RxVm.CanvasInfo);
            Assert.Contains("VP3268-4K", rig.RxVm.CanvasInfo);
            Assert.Contains("Gbps", rig.RxVm.CanvasInfo); // four 1080p60 windows exceed the 9 Gbps guideline
            Assert.Equal(16.0 / 9.0, rig.RxVm.CanvasAspect, 3);
        }

        [Fact]
        public async Task Take_WithoutAnEdid_TheCardShowsTheFallbackAndWhy()
        {
            var rig = new Rig();
            rig.Client.DisplayEdid = null;
            var composer = await rig.Compose("matrix2x2");
            rig.Place(0, rig.Tx1Vm);

            await composer.TakeAsync();

            Assert.Contains("1920x1080@60", rig.RxVm.CanvasInfo);
            Assert.Contains("EDID unavailable", rig.RxVm.CanvasInfo);
        }

        [Fact]
        public async Task Take_OnASixteenByTenDisplay_KeepsItsAspectForTheWindowSurface()
        {
            var rig = new Rig();
            rig.Client.DisplayEdid = TestEdids.Build((1920, 1200, 60, false));
            var composer = await rig.Compose("matrix2x2");
            Assert.Equal(1920, composer.Draft!.Plan.Width);
            Assert.Equal(1200, composer.Draft.Plan.Height);
            rig.Place(0, rig.Tx1Vm);

            await composer.TakeAsync();

            Assert.Equal(1920.0 / 1200.0, rig.RxVm.CanvasAspect, 3);
        }

        [Fact]
        public async Task Take_WhenTheDisplayWasSwappedMeanwhile_RedrawsThenTheNextTakeShowsTheNewOutputOnTheCard()
        {
            var rig = new Rig();
            rig.Client.DisplayEdid = TestEdids.Build((1920, 1080, 60, false));
            var composer = await rig.Compose("matrix2x2");
            rig.Place(1, rig.Tx1Vm);

            rig.Client.DisplayEdid = TestEdids.ViewSonic4k;
            await composer.TakeAsync(); // the display changed: nothing is sent, the blocks are redrawn
            Assert.Equal(BannerKind.Warning, composer.Banner.Kind);
            Assert.False(rig.RxVm.IsMultiView);
            Assert.Equal(3840, composer.Draft!.Plan.Width);

            await composer.TakeAsync();

            Assert.Contains("3840x2160@60", rig.RxVm.CanvasInfo);
            Assert.Equal(4, rig.RxVm.Slots.Count);
            Assert.True(rig.RxVm.Slots.Single(s => s.Index == 1).IsAssigned);
        }

        [Fact]
        public async Task SingleView_AfterChoosingALayoutThatWasNeverTaken_ChangesNoDevice()
        {
            var rig = new Rig();
            rig.Client.DisplayEdid = TestEdids.ViewSonic4k;
            await rig.Compose("matrix2x2");
            rig.Vm.CloseComposer();

            Assert.True(await rig.Vm.ChooseLayoutFromCardAsync(rig.RxVm, "single"));

            Assert.Empty(rig.RxVm.CanvasInfo);
            Assert.False(rig.RxVm.IsMultiView);
            Assert.Empty(rig.Client.MultiViewCalls); // nothing was taken, so the decoder is still in Single View
        }

        [Fact]
        public async Task ChoosingAPreset_OnARoutedDisplay_KeepsItRoutedUntilTake_ThenLeavesSingleView()
        {
            var rig = new Rig();
            await rig.Vm.RouteVideoAsync(rig.Tx1, rig.Rx);

            var composer = await rig.Compose("matrix2x2");

            Assert.Equal("tx1", rig.Rx.ActiveSourceId); // staged only
            Assert.All(composer.Blocks, b => Assert.False(b.IsPlaced)); // spec 5.1: the layout opens with empty blocks

            rig.Place(0, rig.Tx1Vm);
            await composer.TakeAsync();

            var first = rig.RxVm.Slots.Single(s => s.Index == 0);
            Assert.True(first.IsAssigned);
            Assert.Equal("TX1", first.SourceName);
            Assert.Contains(rig.Client.MultiViewCalls, c => c.StartsWith("multiview:rx1:"));
            Assert.Empty(rig.Rx.ActiveSourceId);
        }

        [Fact]
        public async Task Take_Success_FillsTheCardBlock()
        {
            var rig = new Rig();
            var composer = await rig.Compose("matrix2x2");
            rig.Place(2, rig.Tx2Vm);

            await composer.TakeAsync();

            var slot = rig.RxVm.Slots.Single(s => s.Index == 2);
            Assert.True(slot.IsAssigned);
            Assert.Equal("TX2", slot.SourceName);
            Assert.True(rig.RxVm.IsRouted);
            Assert.False(rig.RxVm.HasError);
            Assert.Equal("MATRIX 2X2 · 1/4", rig.RxVm.ModeBadge);
        }

        [Fact]
        public async Task Take_Refused_LeavesTheScreenAndTheCardAsTheyWere_AndKeepsTheDraft()
        {
            var rig = new Rig();
            var composer = await rig.Compose("matrix2x2");
            rig.Place(0, rig.Tx1Vm);
            rig.Client.FailMultiViewPrefix = "multiview:";

            await composer.TakeAsync();

            Assert.Equal(BannerKind.Error, composer.Banner.Kind);
            Assert.Contains("RX1", composer.Banner.Text);
            Assert.Contains("Nothing changed on screen", composer.Banner.Text);
            Assert.False(rig.MultiView.IsMultiView(rig.Rx));
            Assert.False(rig.RxVm.IsMultiView);
            Assert.True(composer.Draft!.Placements.ContainsKey(0));

            rig.Vm.CloseComposer();
            Assert.True(rig.RxVm.HasDraft); // the refused picture is still there to resume
        }

        [Fact]
        public async Task TheSameSourceInASecondEqualBlock_IsAddedNotMoved()
        {
            var rig = new Rig();
            var composer = await rig.Compose("matrix2x2");
            rig.Place(0, rig.Tx1Vm);
            rig.Place(3, rig.Tx1Vm);

            await composer.TakeAsync();

            Assert.Equal("TX1", rig.RxVm.Slots.Single(s => s.Index == 0).SourceName);
            Assert.Equal("TX1", rig.RxVm.Slots.Single(s => s.Index == 3).SourceName);
            Assert.Single(rig.Client.LastSubscriptions!); // one encoder, one subscription read by both windows
        }

        [Fact]
        public async Task TheSameSourceInABlockOfAnotherSize_IsRefusedWithoutHardwareCalls()
        {
            var rig = new Rig();
            var composer = await rig.Compose("quad_1_3");
            rig.Place(0, rig.Tx1Vm); // the big focus block

            composer.Arm(rig.Tx1Vm);
            composer.TapBlock(1);

            Assert.Contains("One encoder has one size per layout", composer.Banner.Text);
            Assert.False(composer.Draft!.Placements.ContainsKey(1));
            Assert.Empty(rig.Client.MultiViewCalls);
        }

        [Fact]
        public async Task SizeConflictWithAnotherDisplay_IsRefusedWithoutHardwareCalls()
        {
            var rig = new Rig();
            var rx2 = new AvasDevice { DeviceId = "rx2", DeviceName = "RX2", IsReceiver = true, IsActive = true };
            var rx2Vm = new DisplayCardViewModel(rx2, rig.Vm.UnrouteVideoAsync);
            rig.Vm.Displays.Add(rx2Vm);
            await rig.TakeMatrixWithTx1Async(); // TX1 runs at 960x540 on RX1

            await rig.Vm.ChooseLayoutFromCardAsync(rx2Vm, "quad_1_3");
            var composer = rig.Composer;
            composer.Arm(rig.Tx1Vm);
            composer.TapBlock(0); // the big block: another size

            Assert.Contains("RX1", composer.Banner.Text);
            Assert.Empty(composer.Draft!.Placements);
            Assert.Empty(rig.Client.MultiViewCalls);
        }

        [Fact]
        public async Task OfflineSource_IsUnavailable_AndANonTransmitterDropIsRefused_WithoutHardwareCalls()
        {
            var rig = new Rig();
            var composer = await rig.Compose("matrix2x2");
            rig.Tx1Vm.SetOnlineState(false);
            var notTx = new AvasDevice { DeviceId = "rx9", DeviceName = "RX9", IsReceiver = true, IsActive = true };

            var offline = composer.CheckPlace(0, rig.Tx1Vm);
            bool dropped = await rig.Vm.DropSourceAsync(rig.RxVm, notTx);

            Assert.False(offline.Ok);
            Assert.Contains("TX1 is offline", offline.Reason);
            Assert.False(dropped);
            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.Null(rig.Client.LastVideoTx);
        }

        [Fact]
        public async Task ComposerWithoutALayout_StartsAtStepOne_AndCannotTake()
        {
            var rig = new Rig();

            await rig.Vm.OpenComposerAsync(rig.RxVm);

            Assert.Null(rig.Composer.Draft);
            Assert.False(rig.Composer.CanTake);
            Assert.Equal("Choose a layout (Step 1).", rig.Composer.TakeBlockedReason);
            Assert.Empty(rig.Client.MultiViewCalls);
        }

        [Fact]
        public async Task Take_ClearsAPreviousCardErrorOnSuccess()
        {
            var rig = new Rig();
            var composer = await rig.Compose("matrix2x2");
            rig.RxVm.LastError = "old";
            rig.Place(0, rig.Tx1Vm);

            await composer.TakeAsync();

            Assert.False(rig.RxVm.HasError);
        }

        [Fact]
        public async Task RouteVideo_OnMultiViewDisplay_ReturnsToGenlockThenJoins()
        {
            var rig = new Rig();
            await rig.TakeMatrixWithTx1Async();

            bool ok = await rig.Vm.RouteVideoAsync(rig.Tx2, rig.Rx);

            Assert.True(ok);
            Assert.Equal(new[] { "genlock:rx1" }, rig.Client.MultiViewCalls);
            Assert.Equal("tx2", rig.Client.LastVideoTx);
            Assert.False(rig.RxVm.IsMultiView);
            Assert.Empty(rig.RxVm.Slots);
            Assert.Equal("tx2", rig.RxVm.ActiveSourceId);
        }

        [Fact]
        public async Task RouteVideo_WhenGenlockIsRefused_DoesNotJoin_AndTheCardKeepsTheMultiView()
        {
            var rig = new Rig();
            await rig.TakeMatrixWithTx1Async();
            rig.Client.FailMultiViewPrefix = "genlock:";

            bool ok = await rig.Vm.RouteVideoAsync(rig.Tx2, rig.Rx);

            Assert.False(ok);
            Assert.Null(rig.Client.LastVideoTx);
            Assert.True(rig.RxVm.IsMultiView);
        }

        [Fact]
        public async Task Unroute_OnMultiViewDisplay_TurnsEverythingOff()
        {
            var rig = new Rig();
            await rig.TakeMatrixWithTx1Async();

            bool ok = await rig.Vm.UnrouteVideoAsync(rig.Rx);

            Assert.True(ok);
            Assert.False(rig.RxVm.IsMultiView);
            Assert.False(rig.RxVm.IsRouted);
            Assert.Equal("rx1", rig.Client.LastUnroutedRx);
        }

        [Fact]
        public async Task SingleViewFromTheCard_OnMultiViewDisplay_ReturnsToSingleView()
        {
            var rig = new Rig();
            await rig.TakeMatrixWithTx1Async();

            bool ok = await rig.Vm.ChooseLayoutFromCardAsync(rig.RxVm, "single");

            Assert.True(ok);
            Assert.Equal(new[] { "genlock:rx1" }, rig.Client.MultiViewCalls);
            Assert.False(rig.RxVm.IsMultiView);
            Assert.Empty(rig.RxVm.CanvasInfo); // the multiview's output format is gone with it
        }

        [Fact]
        public async Task SingleViewFromTheCard_WhenGenlockIsRefused_ReportsItOnTheCard()
        {
            var rig = new Rig();
            await rig.TakeMatrixWithTx1Async();
            rig.Client.FailMultiViewPrefix = "genlock:";

            bool ok = await rig.Vm.ChooseLayoutFromCardAsync(rig.RxVm, "single");

            Assert.False(ok);
            Assert.True(rig.RxVm.IsMultiView);
            Assert.Contains("did not accept the switch back to Single View", rig.RxVm.LastError);
        }

        [Fact]
        public async Task Undo_PutsThePreviousPictureBackOnTheCard()
        {
            var rig = new Rig();
            await rig.TakeMatrixWithTx1Async();
            await rig.Vm.OpenComposerAsync(rig.RxVm);
            rig.Place(1, rig.Tx2Vm);
            await rig.Composer.TakeAsync();
            Assert.True(rig.RxVm.Slots.Single(s => s.Index == 1).IsAssigned);

            await rig.Composer.UndoAsync();

            Assert.False(rig.RxVm.Slots.Single(s => s.Index == 1).IsAssigned);
            Assert.Equal("TX1", rig.RxVm.Slots.Single(s => s.Index == 0).SourceName);
        }

        [Fact]
        public void OffIssuedBeforeATake_WinsFirst_ThenTheTakeRuns() => UiThread.Run(async () =>
        {
            var rig = new Rig();
            await rig.TakeMatrixWithTx1Async();
            await rig.Vm.OpenComposerAsync(rig.RxVm);
            rig.Place(1, rig.Tx2Vm);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Client.DuringCall = e => e.StartsWith("genlock:") && entered.TrySetResult() ? release.Task : Task.CompletedTask;

            var off = rig.Vm.UnrouteVideoAsync(rig.Rx);
            await entered.Task; // Off is in flight
            var take = rig.Composer.TakeAsync();
            await Task.Delay(50);
            Assert.DoesNotContain(rig.Client.MultiViewCalls, c => c.StartsWith("multiview:"));
            release.TrySetResult();
            await Task.WhenAll(off, take);

            Assert.True(await off);
            var calls = rig.Client.MultiViewCalls;
            Assert.True(calls.IndexOf("genlock:rx1") < calls.FindLastIndex(c => c.StartsWith("multiview:")));
            Assert.True(rig.MultiView.IsMultiView(rig.Rx)); // the Take came last, and the card agrees
            Assert.True(rig.RxVm.IsMultiView);
        });

        [Fact]
        public async Task ChoosingAPreset_UnknownId_IsRefused()
        {
            var rig = new Rig();

            Assert.False(await rig.Vm.ChooseLayoutFromCardAsync(rig.RxVm, "nope"));
            Assert.False(rig.RxVm.IsMultiView);
            Assert.Null(rig.Vm.ActiveComposer);
        }
    }
}
