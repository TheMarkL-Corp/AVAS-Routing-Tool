using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>The cards must show what the decoders are really doing, read from the hardware.</summary>
    public class MainViewModelRestoreTests
    {
        private const string Rx1Mac = "74fe483c059e";
        private const string PaxMac = "74fe488b22ca";
        private const string Tx2Mac = "74fe488b07bb";
        private const string VegaMac = "0016c0589c1e";

        private sealed class Rig
        {
            public MockSdvoeClient Client { get; } = new();
            public MainViewModel Vm { get; }
            public AvasDevice Rx1 { get; } = new() { DeviceId = Rx1Mac, DeviceName = "RX1_c059e_SFPA", IsReceiver = true, IsActive = true };
            public AvasDevice Pax { get; } = new() { DeviceId = PaxMac, DeviceName = "PAX355S_SFP A", IsReceiver = true, IsActive = true };
            public AvasDevice Tx2 { get; } = new() { DeviceId = Tx2Mac, DeviceName = "TX2_DUAL LINK_SFPA", VendorId = 105, IsTransmitter = true, IsActive = true };
            public AvasDevice Vega { get; } = new() { DeviceId = VegaMac, DeviceName = "VEGA_1100", VendorId = 192, IsTransmitter = true, IsActive = true };
            public DisplayCardViewModel Rx1Vm { get; }
            public DisplayCardViewModel PaxVm { get; }
            public SourceCardViewModel Tx2Vm { get; }
            public SourceCardViewModel VegaVm { get; }

            public Rig()
            {
                Vm = new MainViewModel(new ConfigService(), Client, new MockRtpReceiver(), new MulticastIpManager(), new RoutingOrchestrator(Client));
                Rx1Vm = new DisplayCardViewModel(Rx1, Vm.UnrouteVideoAsync);
                PaxVm = new DisplayCardViewModel(Pax, Vm.UnrouteVideoAsync);
                Tx2Vm = new SourceCardViewModel(Tx2);
                VegaVm = new SourceCardViewModel(Vega);
                Vm.Displays.Add(Rx1Vm);
                Vm.Displays.Add(PaxVm);
                Vm.Sources.Add(Tx2Vm);
                Vm.Sources.Add(VegaVm);
            }

            public void Hardware(string mac, string settingsFixture, string? layoutFixture = null) =>
                Client.ReceiverStates[mac] = RxStateReader.Parse(Fixtures.Read(settingsFixture), layoutFixture == null ? null : Fixtures.Read(layoutFixture));

            public static RxState ExternalMultiView() =>
                RxStateReader.Parse(Fixtures.Read("rx_multiview.json"), Fixtures.Read("rx_multiview_layout.json"))! with
                {
                    Layout = new RxLayout("compatibility_4k_2x2", 3840, 2160, new[] { new RxWindow(0, 1), new RxWindow(1, 2) })
                };
        }

        // ---- Single View ------------------------------------------------------------------

        [Fact]
        public async Task SingleViewRoute_IsRestoredAndThePreviewMirrored()
        {
            var rig = new Rig();
            rig.Hardware(Rx1Mac, "rx_single_view.json");

            await rig.Vm.RestoreStateAsync();

            Assert.True(rig.Rx1Vm.IsRouted);
            Assert.Equal(Tx2Mac, rig.Rx1Vm.ActiveSourceId);
            Assert.Equal("TX2_DUAL LINK_SFPA", rig.Rx1Vm.ActiveSourceName);
            Assert.Equal("SINGLE VIEW", rig.Rx1Vm.ModeBadge);
            Assert.True(rig.Rx1Vm.IsPreviewSupported);

            var bmp = new WriteableBitmap(16, 16, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null);
            rig.Tx2Vm.PreviewBitmap = bmp;
            Assert.Same(bmp, rig.Rx1Vm.PreviewBitmap);
        }

        [Fact]
        public async Task SingleViewRouteToASourceTheAppDoesNotKnow_StillShowsItsName()
        {
            var rig = new Rig();
            rig.Vm.Sources.Clear();
            rig.Hardware(Rx1Mac, "rx_single_view.json");

            await rig.Vm.RestoreStateAsync();

            Assert.True(rig.Rx1Vm.IsRouted);
            Assert.Equal("TX2_DUAL LINK_SFPA", rig.Rx1Vm.ActiveSourceName);
            Assert.False(rig.Rx1Vm.IsPreviewSupported);
        }

        [Fact]
        public async Task UnroutedReceiver_StaysUnrouted_AndHardwareOverridesALocalRoute()
        {
            var rig = new Rig();
            rig.Pax.ActiveSourceId = Tx2Mac; // the card believed it was routed
            rig.Pax.ActiveSourceName = "TX2_DUAL LINK_SFPA";
            rig.PaxVm.NotifyRoutingChanged();
            rig.Hardware(PaxMac, "rx_unrouted.json");

            await rig.Vm.RestoreStateAsync();

            Assert.False(rig.PaxVm.IsRouted);
            Assert.Empty(rig.Pax.ActiveSourceId);
            Assert.Equal("UNROUTED", rig.PaxVm.ModeBadge);
        }

        // ---- MultiView ---------------------------------------------------------------------

        [Fact]
        public async Task LayoutThisToolCreated_IsRestoredWithItsWindowsAndSources()
        {
            var rig = new Rig();
            rig.Hardware(Rx1Mac, "rx_multiview.json", "rx_multiview_layout.json");

            await rig.Vm.RestoreStateAsync();

            Assert.True(rig.Rx1Vm.IsMultiView);
            Assert.Equal("matrix2x2", rig.Rx1Vm.LayoutId);
            Assert.Equal(4, rig.Rx1Vm.Slots.Count);
            Assert.Equal("TX2_DUAL LINK_SFPA", rig.Rx1Vm.Slots.Single(s => s.Index == 0).SourceName);
            Assert.Equal("VEGA_1100", rig.Rx1Vm.Slots.Single(s => s.Index == 1).SourceName);
            Assert.False(rig.Rx1Vm.Slots.Single(s => s.Index == 2).IsAssigned);
            Assert.Contains("1920x1080@60", rig.Rx1Vm.CanvasInfo);
            Assert.True(rig.Rx1Vm.IsRouted);
            Assert.Empty(rig.Client.MultiViewCalls); // restoring never changes a device
        }

        [Fact]
        public async Task AfterARestore_TheComposerStartsFromTheScreen_AndATakeKeepsTheRestoredSources()
        {
            var rig = new Rig();
            rig.Hardware(Rx1Mac, "rx_multiview.json", "rx_multiview_layout.json");
            await rig.Vm.RestoreStateAsync();
            var tx3 = new SourceCardViewModel(new AvasDevice { DeviceId = "tx3", DeviceName = "TX3", IsTransmitter = true, IsActive = true });
            rig.Vm.Sources.Add(tx3);

            await rig.Vm.OpenComposerAsync(rig.Rx1Vm);
            var composer = rig.Vm.ActiveComposer!;
            Assert.Equal(2, composer.Draft!.Placements.Count);
            composer.Arm(tx3);
            composer.TapBlock(2);
            await composer.TakeAsync();

            Assert.Equal(BannerKind.Success, composer.Banner.Kind);
            Assert.Equal(3, rig.Client.LastSubscriptions!.Count); // the two restored sources stay
            // the legacy layout found running is rewritten under this display's own name
            Assert.Contains($"layout:avas_{Rx1Mac}_matrix2x2_1920x1080", rig.Client.MultiViewCalls);
            Assert.Equal(1, rig.Client.EdidReads); // the display is checked before the hardware is touched
            Assert.Equal(3, rig.Rx1Vm.Slots.Count(s => s.IsAssigned));
        }

        [Fact]
        public async Task SharedSurfaceLayout_FillsEveryBlockReadingTheSubscription()
        {
            var rig = new Rig();
            var state = RxStateReader.Parse(Fixtures.Read("rx_multiview_shared_surface.json"), Fixtures.Read("rx_multiview_shared_surface_layout.json"))!;
            rig.Client.ReceiverStates[Rx1Mac] = state with { Layout = state.Layout! with { Name = $"avas_{Rx1Mac}_matrix2x2_1920x1080" } };

            await rig.Vm.RestoreStateAsync();

            Assert.True(rig.Rx1Vm.IsMultiView);
            string block1 = rig.Rx1Vm.Slots.Single(s => s.Index == 0).SourceName;
            Assert.NotEmpty(block1);
            Assert.Equal(block1, rig.Rx1Vm.Slots.Single(s => s.Index == 2).SourceName);
            Assert.False(rig.Rx1Vm.Slots.Single(s => s.Index == 3).IsAssigned); // a black window
            Assert.Empty(rig.Client.MultiViewCalls);
        }

        [Fact]
        public async Task LayoutFromElsewhere_IsShownReadOnlyAsAnExternalLayout()
        {
            var rig = new Rig();
            rig.Client.ReceiverStates[Rx1Mac] = Rig.ExternalMultiView();

            await rig.Vm.RestoreStateAsync();

            Assert.False(rig.Rx1Vm.IsMultiView);
            Assert.Empty(rig.Rx1Vm.Slots);
            Assert.True(rig.Rx1Vm.IsExternalLayout);
            Assert.Equal("EXTERNAL LAYOUT", rig.Rx1Vm.ModeBadge);
            Assert.Equal("2 IN LAYOUT", rig.Rx1Vm.SourceLabel);
            Assert.Contains("compatibility_4k_2x2", rig.Rx1Vm.ExternalLayoutText);
            Assert.Contains("TX2_DUAL LINK_SFPA", rig.Rx1Vm.ExternalLayoutText);
            Assert.True(rig.Rx1Vm.IsRouted);
        }

        [Fact]
        public async Task HardwareOverridesALocalLayout_WhenTheDecoderIsNowInSingleView()
        {
            var rig = new Rig();
            await rig.Vm.OpenComposerAsync(rig.Rx1Vm, "matrix2x2");
            rig.Vm.ActiveComposer!.Arm(rig.Tx2Vm);
            rig.Vm.ActiveComposer.TapBlock(0);
            await rig.Vm.ActiveComposer.TakeAsync();
            rig.Vm.CloseComposer();
            Assert.True(rig.Rx1Vm.IsMultiView);
            rig.Hardware(Rx1Mac, "rx_single_view.json"); // someone switched it in the BAVM console

            await rig.Vm.RestoreStateAsync();

            Assert.False(rig.Rx1Vm.IsMultiView);
            Assert.Empty(rig.Rx1Vm.Slots);
            Assert.Equal(Tx2Mac, rig.Rx1Vm.ActiveSourceId);
            // the orchestrator forgot the layout too: a new Composer has nothing on screen to start from
            await rig.Vm.OpenComposerAsync(rig.Rx1Vm);
            Assert.Null(rig.Vm.ActiveComposer!.Draft);
            Assert.Empty(rig.Vm.ActiveComposer.LiveBlocks);
        }

        [Fact]
        public async Task RestoringTwice_GivesTheSameCard()
        {
            var rig = new Rig();
            rig.Hardware(Rx1Mac, "rx_multiview.json", "rx_multiview_layout.json");

            await rig.Vm.RestoreStateAsync();
            await rig.Vm.RestoreStateAsync();

            Assert.Equal(4, rig.Rx1Vm.Slots.Count);
            Assert.Equal(2, rig.Rx1Vm.Slots.Count(s => s.IsAssigned));
        }

        // ---- Robustness ---------------------------------------------------------------------

        [Fact]
        public async Task UnreadableReceiver_StaysUnrouted_AndTheOthersStillRestore()
        {
            var rig = new Rig();
            rig.Hardware(PaxMac, "rx_single_view.json");
            // RX1 has no entry: unreadable

            await rig.Vm.RestoreStateAsync();

            Assert.False(rig.Rx1Vm.IsRouted);
            Assert.True(rig.PaxVm.IsRouted);
            Assert.Equal(2, rig.Client.ReceiverReads.Count);
        }

        [Fact]
        public async Task OfflineReceivers_AreNotRead()
        {
            var rig = new Rig();
            rig.PaxVm.SetOnlineState(false);
            rig.Hardware(Rx1Mac, "rx_single_view.json");

            await rig.Vm.RestoreStateAsync();

            Assert.DoesNotContain(PaxMac, rig.Client.ReceiverReads);
        }

        [Fact]
        public async Task ReceiversAreReadInParallel()
        {
            var rig = new Rig();
            rig.Client.ReceiverReadDelay[Rx1Mac] = TimeSpan.FromMilliseconds(400);
            rig.Client.ReceiverReadDelay[PaxMac] = TimeSpan.FromMilliseconds(400);
            rig.Hardware(Rx1Mac, "rx_single_view.json");
            rig.Hardware(PaxMac, "rx_unrouted.json");

            var sw = Stopwatch.StartNew();
            await rig.Vm.RestoreStateAsync();
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 750, $"took {sw.ElapsedMilliseconds} ms - reads are not parallel");
        }

        // ---- Leaving an external multiview ----------------------------------------------------

        [Fact]
        public async Task JoinOnAnExternalMultiView_GenlocksFirst()
        {
            var rig = new Rig();
            rig.Client.ReceiverStates[Rx1Mac] = Rig.ExternalMultiView();
            await rig.Vm.RestoreStateAsync();

            bool ok = await rig.Vm.RouteVideoAsync(rig.Tx2, rig.Rx1);

            Assert.True(ok);
            Assert.Equal(new[] { $"genlock:{Rx1Mac}" }, rig.Client.MultiViewCalls);
            Assert.Equal(Tx2Mac, rig.Client.LastVideoTx);
            Assert.False(rig.Rx1Vm.IsExternalLayout);
            Assert.Equal("SINGLE VIEW", rig.Rx1Vm.ModeBadge);
        }

        [Fact]
        public async Task JoinOnAnExternalMultiView_DoesNotJoinWhenGenlockIsRefused()
        {
            var rig = new Rig();
            rig.Client.ReceiverStates[Rx1Mac] = Rig.ExternalMultiView();
            await rig.Vm.RestoreStateAsync();
            rig.Client.FailMultiViewPrefix = "genlock:";

            bool ok = await rig.Vm.RouteVideoAsync(rig.Tx2, rig.Rx1);

            Assert.False(ok);
            Assert.Null(rig.Client.LastVideoTx);
            Assert.True(rig.Rx1Vm.IsExternalLayout);
        }

        [Fact]
        public async Task TurnOffAnExternalMultiView_LeavesItsSubscriptionsThenGenlocks()
        {
            var rig = new Rig();
            rig.Client.ReceiverStates[Rx1Mac] = Rig.ExternalMultiView();
            await rig.Vm.RestoreStateAsync();

            bool ok = await rig.Vm.UnrouteVideoAsync(rig.Rx1);

            Assert.True(ok);
            Assert.Equal(Rx1Mac, rig.Client.LastUnroutedRx);
            Assert.Equal(new[] { $"genlock:{Rx1Mac}" }, rig.Client.MultiViewCalls);
            Assert.False(rig.Rx1Vm.IsExternalLayout);
            Assert.False(rig.Rx1Vm.IsRouted);
        }

        [Fact]
        public async Task SingleViewTileOnAnExternalMultiView_GenlocksAndClearsTheBadge()
        {
            var rig = new Rig();
            rig.Client.ReceiverStates[Rx1Mac] = Rig.ExternalMultiView();
            await rig.Vm.RestoreStateAsync();

            bool ok = await rig.Vm.ChooseLayoutFromCardAsync(rig.Rx1Vm, "single");

            Assert.True(ok);
            Assert.Equal(new[] { $"genlock:{Rx1Mac}" }, rig.Client.MultiViewCalls);
            Assert.False(rig.Rx1Vm.IsExternalLayout);
        }

        [Fact]
        public async Task ChoosingAPresetOverAnExternalMultiView_StagesIt_AndTheFirstTakeReplacesTheBadge()
        {
            var rig = new Rig();
            rig.Client.ReceiverStates[Rx1Mac] = Rig.ExternalMultiView();
            await rig.Vm.RestoreStateAsync();

            bool ok = await rig.Vm.ChooseLayoutFromCardAsync(rig.Rx1Vm, "matrix2x2");

            Assert.True(ok);
            Assert.True(rig.Rx1Vm.IsExternalLayout); // staged only: the screen still runs the external layout
            Assert.Empty(rig.Client.MultiViewCalls);
            var composer = rig.Vm.ActiveComposer!;
            Assert.Equal(4, composer.Blocks.Count);

            composer.Arm(rig.Tx2Vm);
            composer.TapBlock(0);
            await composer.TakeAsync();

            Assert.False(rig.Rx1Vm.IsExternalLayout);
            Assert.True(rig.Rx1Vm.IsMultiView);
            Assert.Equal(4, rig.Rx1Vm.Slots.Count);
            Assert.Contains($"{Rx1Mac}:2", rig.Client.Leaves); // the external layout's other subscription was left
        }

        // ---- subscription 0, and the external layout's encoders -----------------------------------

        /// <summary>RX1 runs a layout from elsewhere: TX2 (subscription 0) in a 960x540 window, VEGA (subscription 1) in another.</summary>
        private static RxState ExternalWithSubscriptionZero() =>
            Rig.ExternalMultiView() with
            {
                Subscriptions = new[]
                {
                    new RxSubscription(0, true, "STREAMING", Tx2Mac, "TX2_DUAL LINK_SFPA", 1),
                    new RxSubscription(1, true, "STREAMING", VegaMac, "VEGA_1100", 1),
                },
                Layout = new RxLayout("compatibility_4k_2x2", 3840, 2160, new[]
                {
                    new RxWindow(0, 0, 0, 0, 960, 540),
                    new RxWindow(1, 1, 960, 0, 960, 540),
                }),
            };

        private static int MainBlock(MainViewModel vm) =>
            vm.ActiveComposer!.Draft!.Plan.Slots.Single(s => s.Label == "Main").Index;

        [Fact]
        public async Task SingleViewFromTheCardMenu_OnAnExternalLayout_LeavesSubscriptionZeroToo_WhenNoSourceFollows()
        {
            var rig = new Rig();
            rig.Client.ReceiverStates[Rx1Mac] = ExternalWithSubscriptionZero();
            await rig.Vm.RestoreStateAsync();

            Assert.True(await rig.Vm.ChooseLayoutFromCardAsync(rig.Rx1Vm, "single"));

            Assert.Equal(new[] { $"{Rx1Mac}:0", $"{Rx1Mac}:1" }, rig.Client.Leaves.OrderBy(x => x).ToArray());
            Assert.False(rig.Rx1Vm.IsRouted);
        }

        [Fact]
        public async Task ARouteOverAnExternalLayout_StillKeepsSubscriptionZeroForTheJoin()
        {
            var rig = new Rig();
            rig.Client.ReceiverStates[Rx1Mac] = ExternalWithSubscriptionZero();
            await rig.Vm.RestoreStateAsync();

            Assert.True(await rig.Vm.RouteVideoAsync(rig.Tx2, rig.Rx1));

            Assert.Equal(new[] { $"{Rx1Mac}:1" }, rig.Client.Leaves);
        }

        [Fact]
        public async Task AnExternalLayoutOnOneDisplay_BlocksAnotherDisplayFromScalingItsEncoderToAnotherSize()
        {
            var rig = new Rig();
            rig.Client.ReceiverStates[Rx1Mac] = ExternalWithSubscriptionZero();
            await rig.Vm.RestoreStateAsync();

            await rig.Vm.OpenComposerAsync(rig.PaxVm, "pip");
            var composer = rig.Vm.ActiveComposer!;
            int main = MainBlock(rig.Vm);

            var check = composer.CheckPlace(main, rig.Tx2Vm); // TX2 is read at 960x540 by RX1's layout

            Assert.False(check.Ok);
            Assert.Contains("TX2_DUAL LINK_SFPA runs at 960x540 on RX1_c059e_SFPA", check.Reason);
            composer.Arm(rig.Tx2Vm);
            composer.TapBlock(main);
            Assert.False(composer.Blocks.Single(b => b.Index == main).IsPlaced);
        }

        [Fact]
        public async Task WhenTheExternalLayoutIsLeft_ItsEncodersAreFreeAgain()
        {
            var rig = new Rig();
            rig.Client.ReceiverStates[Rx1Mac] = ExternalWithSubscriptionZero();
            await rig.Vm.RestoreStateAsync();
            await rig.Vm.RouteVideoAsync(rig.Tx2, rig.Rx1);

            await rig.Vm.OpenComposerAsync(rig.PaxVm, "pip");

            Assert.True(rig.Vm.ActiveComposer!.CheckPlace(MainBlock(rig.Vm), rig.Tx2Vm).Ok);
        }
    }
}
