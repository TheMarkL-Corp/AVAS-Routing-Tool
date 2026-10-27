using System.Linq;
using System.Threading.Tasks;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class MultiViewRestoreTests
    {
        private static RxState MultiViewState() =>
            RxStateReader.Parse(Fixtures.Read("rx_multiview.json"), Fixtures.Read("rx_multiview_layout.json"))!;

        [Fact]
        public void Analyze_SingleViewReceiver_IsNotMultiView()
        {
            Assert.Null(MultiViewRestore.Analyze(RxStateReader.Parse(Fixtures.Read("rx_single_view.json"))!));
        }

        [Fact]
        public void Analyze_LayoutThisToolCreated_RebuildsThePlanAndMapsSubscriptionsToWindows()
        {
            var restored = MultiViewRestore.Analyze(MultiViewState())!;

            Assert.True(restored.IsRecognised);
            Assert.Equal("avas_matrix2x2_1920x1080", restored.LayoutName);
            Assert.Equal("matrix2x2", restored.Plan!.Id);
            Assert.Equal((1920, 1080, 60), (restored.Canvas.Width, restored.Canvas.Height, restored.Canvas.Fps));

            // subscription 1 (TX2) feeds surface 1 = window 0; subscription 2 (VEGA) feeds window 1
            var sources = restored.Sources.OrderBy(s => s.SlotIndex).ToList();
            Assert.Equal((0, "74fe488b07bb", "TX2_DUAL LINK_SFPA"), (sources[0].SlotIndex, sources[0].Mac, sources[0].Name));
            Assert.Equal((1, "0016c0589c1e", "VEGA_1100"), (sources[1].SlotIndex, sources[1].Mac, sources[1].Name));
            Assert.Equal(new[] { 1, 2 }, restored.SubscriptionIndexes.OrderBy(i => i).ToArray());

            var draft = restored.Draft!;
            Assert.Same(restored.Plan, draft.Plan);
            Assert.Equal(new[] { 0, 1 }, draft.Placements.Keys.OrderBy(k => k).ToArray());
            Assert.Equal("VEGA_1100", draft.Placements[1].SourceName);
        }

        [Fact]
        public void Analyze_LayoutNotCreatedByThisTool_IsExternal()
        {
            var state = MultiViewState() with
            {
                Layout = new RxLayout("compatibility_4k_2x2", 3840, 2160, new[] { new RxWindow(0, 0), new RxWindow(1, 1) })
            };

            var restored = MultiViewRestore.Analyze(state)!;

            Assert.False(restored.IsRecognised);
            Assert.Null(restored.Plan);
            Assert.Null(restored.Draft);
            Assert.Equal("compatibility_4k_2x2", restored.LayoutName);
            Assert.Equal((3840, 2160), (restored.Canvas.Width, restored.Canvas.Height));
            Assert.Equal(2, restored.Sources.Count);
            Assert.Equal(new[] { 1, 2 }, restored.SubscriptionIndexes.OrderBy(i => i).ToArray());
        }

        [Fact]
        public void Analyze_LayoutUnreadable_IsExternalWithTheOutputOfTheDecoder()
        {
            var state = RxStateReader.Parse(Fixtures.Read("rx_multiview.json"))!; // no layout reply

            var restored = MultiViewRestore.Analyze(state)!;

            Assert.False(restored.IsRecognised);
            Assert.Equal(string.Empty, restored.LayoutName);
            Assert.Equal(state.Width, restored.Canvas.Width);
            Assert.Equal(2, restored.Sources.Count);
        }

        [Fact]
        public void Analyze_SubscriptionWithoutAWindow_IsDroppedFromTheRecognisedMapping()
        {
            var state = MultiViewState() with
            {
                Layout = new RxLayout("avas_matrix2x2_1920x1080", 1920, 1080, new[] { new RxWindow(0, 1) }) // surface 2 has no window
            };

            var restored = MultiViewRestore.Analyze(state)!;

            Assert.True(restored.IsRecognised);
            Assert.Single(restored.Sources);
            Assert.Equal("TX2_DUAL LINK_SFPA", restored.Sources[0].Name);
            Assert.Equal(new[] { 1, 2 }, restored.SubscriptionIndexes.OrderBy(i => i).ToArray()); // still all to leave on Off
        }

        [Fact]
        public void Analyze_LayoutNameFromThisToolButWindowIndexNotInThePlan_IsDropped()
        {
            var state = MultiViewState() with
            {
                Layout = new RxLayout("avas_matrix2x2_1920x1080", 1920, 1080, new[] { new RxWindow(9, 1), new RxWindow(1, 2) })
            };

            var restored = MultiViewRestore.Analyze(state)!;

            Assert.Single(restored.Sources);
            Assert.Equal(1, restored.Sources[0].SlotIndex);
        }
    }

    public class MultiViewOrchestratorAdoptTests
    {
        private static AvasDevice Rx(string mac = "rx1") =>
            new() { DeviceId = mac, DeviceName = "RX " + mac, IsReceiver = true, IsActive = true };

        private static (MultiViewOrchestrator o, MockSdvoeClient c) Create()
        {
            var c = new MockSdvoeClient();
            return (new MultiViewOrchestrator(c), c);
        }

        private static MultiViewCanvas Canvas(int w, int h, int fps) => new(w, h, fps, false, $"{w}x{h}@{fps} (running)");

        private static MultiViewDraft Live(int w, int h, params (int Slot, string Mac)[] placements)
        {
            var draft = new MultiViewDraft(MultiViewPresets.Build("matrix2x2", w, h));
            foreach (var (slot, mac) in placements) draft.Place(slot, mac, mac.ToUpperInvariant());
            return draft;
        }

        [Fact]
        public void Adopt_RegistersTheSessionWithoutTouchingAnyDevice()
        {
            var (o, c) = Create();
            var rx = Rx();

            o.Adopt(rx, Live(1920, 1080, (0, "tx1")), Canvas(1920, 1080, 60), new[] { 1 });

            Assert.True(o.IsMultiView(rx));
            Assert.Equal("tx1", o.GetLive(rx)!.Placements[0].SourceMac);
            Assert.False(o.CanUndo(rx));
            Assert.Empty(c.MultiViewCalls);
            Assert.Equal(0, c.EdidReads);
        }

        [Fact]
        public async Task TakeAfterAdopt_KeepsTheRestoredSourceAndRewritesTheLayoutUnderThisDisplaysName()
        {
            var (o, c) = Create();
            var rx = Rx();
            var restored = Live(1920, 1080, (0, "tx1"));
            o.Adopt(rx, restored, Canvas(1920, 1080, 60), new[] { 1 });
            var next = o.GetLive(rx)!;
            next.Place(1, "tx2", "TX2");

            var result = await o.TakeAsync(rx, next);

            Assert.Equal(TakeOutcome.Applied, result.Outcome);
            Assert.Contains("layout:avas_rx1_matrix2x2_1920x1080", c.MultiViewCalls);
            Assert.Equal(1, c.EdidReads); // the display is checked before the hardware is touched
            Assert.Equal(2, c.LastSubscriptions!.Count); // the restored source stays
            Assert.True(o.CanUndo(rx)); // back to what was found running
        }

        [Fact]
        public async Task AdoptedHeavyLayout_NeverRestoresDividersItDidNotSet()
        {
            var (o, c) = Create();
            var rx = Rx();
            o.Adopt(rx, Live(3840, 2160, (0, "tx1")), Canvas(3840, 2160, 60), new[] { 1 }); // heavy at 60 fps

            await o.TurnOffAsync(rx);

            Assert.DoesNotContain(c.MultiViewCalls, x => x.StartsWith("divider:"));
        }

        [Fact]
        public async Task AdoptedSession_CanReturnToSingleView()
        {
            var (o, c) = Create();
            var rx = Rx();
            o.Adopt(rx, Live(1920, 1080), Canvas(1920, 1080, 60), System.Array.Empty<int>());

            Assert.True(await o.ReturnToSingleViewAsync(rx));

            Assert.Equal(new[] { "fastswitch:rx1:1920x1080@60" }, c.MultiViewCalls);
            Assert.False(o.IsMultiView(rx));
        }

        [Fact]
        public void AdoptedLayout_CountsForTheOtherDisplays()
        {
            var (o, _) = Create();
            o.Adopt(Rx("rx1"), Live(1920, 1080, (0, "tx1")), Canvas(1920, 1080, 60), new[] { 1 });

            var use = Assert.Single(o.UsesExcept(Rx("rx2")));

            Assert.Equal(("RX rx1", "tx1", 960, 540), (use.DisplayName, use.SourceMac, use.Width, use.Height));
        }

        // ---- subscriptions really joined ---------------------------------------------------------------

        [Fact]
        public async Task TurnOff_AfterAdoptingALegacyLayoutWithOnlyBlock4Filled_LeavesSubscription4()
        {
            var (o, c) = Create();
            var rx = Rx();
            // legacy layouts gave each window its own surface: block 4 reads surface 4
            o.Adopt(rx, Live(1920, 1080, (3, "tx1")), Canvas(1920, 1080, 60), new[] { 4 });

            Assert.True(await o.TurnOffAsync(rx));

            Assert.Equal(new[] { "rx1:4" }, c.Leaves);
        }

        [Fact]
        public async Task TakeAfterAdopt_LeavesTheLegacySubscriptionThatDroppedOut()
        {
            var (o, c) = Create();
            var rx = Rx();
            o.Adopt(rx, Live(1920, 1080, (3, "tx1")), Canvas(1920, 1080, 60), new[] { 4 });
            var next = o.GetLive(rx)!;
            next.Place(0, "tx2", "TX2");

            await o.TakeAsync(rx, next); // now tx2@1, tx1@2

            Assert.Equal("multiview:rx1:avas_rx1_matrix2x2_1920x1080:tx2@1,tx1@2", c.MultiViewCalls.Last(x => x.StartsWith("multiview:")));
            Assert.Equal(new[] { "rx1:4" }, c.Leaves);
            c.Leaves.Clear();

            await o.TurnOffAsync(rx);
            Assert.Equal(new[] { "rx1:1", "rx1:2" }, c.Leaves);
        }

        [Fact]
        public async Task ExitExternal_LeavesTheGivenSubscriptionsThenSetsFastSwitch()
        {
            var (o, c) = Create();
            var rx = Rx();

            bool ok = await o.ExitExternalAsync(rx, new[] { 1, 2 });

            Assert.True(ok);
            Assert.Equal("rx1", c.LastUnroutedRx);
            Assert.Equal(new[] { "fastswitch:rx1:1920x1080@60" }, c.MultiViewCalls);
        }

        [Fact]
        public async Task ExitExternal_WithNothingToLeave_JustSetsFastSwitch()
        {
            var (o, c) = Create();

            Assert.True(await o.ExitExternalAsync(Rx(), System.Array.Empty<int>()));

            Assert.Null(c.LastUnroutedRx);
            Assert.Equal(new[] { "fastswitch:rx1:1920x1080@60" }, c.MultiViewCalls);
        }

        [Fact]
        public async Task ExitExternal_FastSwitchRefused_ReportsFailure()
        {
            var (o, c) = Create();
            c.FailMultiViewPrefix = "fastswitch:";

            Assert.False(await o.ExitExternalAsync(Rx(), new[] { 1 }));
        }

        // ---- external layouts: only the scaled stream constrains an encoder ----------------------

        private static readonly RxLayout TwoWindows = new("bavm_layout", 3840, 2160, new[]
        {
            new RxWindow(0, 0, 0, 0, 960, 540),
            new RxWindow(1, 1, 960, 0, 1280, 720),
        });

        [Fact]
        public void ExternalUses_CountOnlyStreamOneWindows()
        {
            var subscriptions = new[]
            {
                new RxSubscription(0, true, "STREAMING", "tx2", "TX2", 0), // the full-size stream: no scaler involved
                new RxSubscription(1, true, "STREAMING", "tx3", "TX3", 1),
            };

            var uses = MultiViewRestore.ExternalUses("RX1", TwoWindows, subscriptions);

            Assert.Equal(new OtherDisplayUse("RX1", "tx3", 1280, 720), Assert.Single(uses));
        }

        [Fact]
        public async Task AStreamZeroWindowOfAnExternalLayout_DoesNotBlockAnotherSizeOnAnotherDisplay_AStreamOneWindowDoes()
        {
            var (o, _) = Create();
            var rx1 = Rx("rx1");
            var rx2 = Rx("rx2");
            var subscriptions = new[]
            {
                new RxSubscription(0, true, "STREAMING", "tx2", "TX2", 0),
                new RxSubscription(1, true, "STREAMING", "tx3", "TX3", 1),
            };
            o.AdoptExternal(rx1, new[] { 0, 1 }, MultiViewRestore.ExternalUses(rx1.DeviceName, TwoWindows, subscriptions));

            var uses = o.UsesExcept(rx2).ToList();
            Assert.Equal("tx3", Assert.Single(uses).SourceMac);

            var (plan, _) = await o.PlanForDisplayAsync(rx2, "pip");
            var main = plan.Slots.Single(s => s.Label == "Main").Index; // 1920x1080
            var pip = new MultiViewDraft(plan);
            Assert.True(pip.Place(main, "tx2", "TX2").Ok);
            Assert.Equal(TakeOutcome.Applied, (await o.TakeAsync(rx2, pip)).Outcome); // TX2 is only on stream 0 there

            var rx3 = Rx("rx3");
            var other = new MultiViewDraft((await o.PlanForDisplayAsync(rx3, "pip")).Plan);
            other.Place(main, "tx3", "TX3");
            var result = await o.TakeAsync(rx3, other);
            Assert.Equal(TakeOutcome.Rejected, result.Outcome); // TX3 runs at 1280x720 on RX1 (stream 1)
            Assert.Contains("1280x720", result.Error);
        }

        [Fact]
        public async Task AFailedTake_OverAnExternalLayout_PutsItsResizedEncodersBackAtTheExternalSize()
        {
            var (o, c) = Create();
            var rx1 = Rx("rx1");
            o.AdoptExternal(rx1, new[] { 1 }, new[] { new OtherDisplayUse("RX rx1", "tx2", 960, 540) });
            var (plan, _) = await o.PlanForDisplayAsync(rx1, "pip");
            var pip = new MultiViewDraft(plan);
            pip.Place(plan.Slots.Single(s => s.Label == "Main").Index, "tx2", "TX2"); // 1920x1080: resizes TX2's scaler
            c.MultiViewCalls.Clear();
            c.FailMultiViewPrefix = "multiview:";

            var result = await o.TakeAsync(rx1, pip);

            Assert.Equal(TakeOutcome.Rejected, result.Outcome);
            Assert.Equal(new[] { "scaler:tx2:1920x1080", "scaler:tx2:960x540" },
                c.MultiViewCalls.Where(x => x.StartsWith("scaler:")).ToArray());
        }

        [Fact]
        public async Task AFailedTake_OverAnExternalLayout_LeavesAnEncoderAlreadyAtTheExternalSizeAlone()
        {
            var (o, c) = Create();
            var rx1 = Rx("rx1");
            o.AdoptExternal(rx1, new[] { 1 }, new[] { new OtherDisplayUse("RX rx1", "tx2", 960, 540) });
            var (plan, _) = await o.PlanForDisplayAsync(rx1, "matrix2x2");
            var matrix = new MultiViewDraft(plan);
            matrix.Place(0, "tx2", "TX2"); // 960x540 at the 1080p fallback: the same size
            c.MultiViewCalls.Clear();
            c.FailMultiViewPrefix = "multiview:";

            await o.TakeAsync(rx1, matrix);

            Assert.Equal(new[] { "scaler:tx2:960x540" }, c.MultiViewCalls.Where(x => x.StartsWith("scaler:")).ToArray());
        }
    }
}
