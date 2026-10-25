using System.Linq;
using System.Threading.Tasks;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class MultiViewOrchestratorTests
    {
        private static AvasDevice Rx(string mac = "rx1") =>
            new() { DeviceId = mac, DeviceName = "RX " + mac, IsReceiver = true, IsActive = true };

        private static (MultiViewOrchestrator o, MockSdvoeClient c) Create()
        {
            var c = new MockSdvoeClient();
            return (new MultiViewOrchestrator(c), c);
        }

        /// <summary>A draft on the plan the display's EDID gives; names are the MAC in capitals unless given.</summary>
        private static async Task<MultiViewDraft> DraftFor(MultiViewOrchestrator o, AvasDevice rx, string layoutId,
            params (int Slot, string Mac)[] placements)
        {
            var (plan, _) = await o.PlanForDisplayAsync(rx, layoutId);
            var draft = new MultiViewDraft(plan);
            foreach (var (slot, mac) in placements)
                Assert.True(draft.Place(slot, mac, mac.ToUpperInvariant()).Ok);
            return draft;
        }

        private static async Task<TakeResult> TakeAsync(MultiViewOrchestrator o, AvasDevice rx, string layoutId,
            params (int Slot, string Mac)[] placements) =>
            await o.TakeAsync(rx, await DraftFor(o, rx, layoutId, placements));

        // ---- planning for the display -----------------------------------------------------------

        [Fact]
        public async Task PlanForDisplay_ReturnsThePlanAndChangesNothing()
        {
            var (o, c) = Create();
            var rx = Rx();

            var (plan, _) = await o.PlanForDisplayAsync(rx, "matrix2x2");

            Assert.Equal("avas_matrix2x2_1920x1080", plan.Name);
            Assert.False(o.IsMultiView(rx)); // no session until something is taken
            Assert.Null(o.GetLive(rx));
            Assert.Empty(c.MultiViewCalls);
        }

        [Fact]
        public async Task PlanForDisplay_ReadsTheDisplayEdidAndSizesTheCanvasToIt()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;

            var (plan, canvas) = await o.PlanForDisplayAsync(Rx(), "matrix2x2");

            Assert.Equal(1, c.EdidReads);
            Assert.Equal("avas_matrix2x2_3840x2160", plan.Name);
            Assert.Equal((1920, 1080), (plan.Slots[0].Width, plan.Slots[0].Height));
            Assert.True(canvas.FromDisplay);
            Assert.Equal((3840, 2160, 60), (canvas.Width, canvas.Height, canvas.Fps));
            Assert.Contains("VP3268-4K", canvas.Description);
        }

        [Fact]
        public async Task PlanForDisplay_WithoutAnEdid_FallsBackToFullHd()
        {
            var (o, c) = Create();
            c.DisplayEdid = null;

            var (plan, canvas) = await o.PlanForDisplayAsync(Rx(), "matrix2x2");

            Assert.Equal("avas_matrix2x2_1920x1080", plan.Name);
            Assert.False(canvas.FromDisplay);
            Assert.Contains("EDID unavailable", canvas.Description);
        }

        // ---- the EDID at Take -----------------------------------------------------------------------

        [Fact]
        public async Task Take_ReadsTheEdidEveryTime()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            var rx = Rx();
            var draft = await DraftFor(o, rx, "matrix2x2", (0, "tx1"));

            await o.TakeAsync(rx, draft);
            draft.Place(1, "tx2", "TX2");
            await o.TakeAsync(rx, draft);

            Assert.Equal(3, c.EdidReads); // once to draw the layout, once before each hardware apply
        }

        [Fact]
        public async Task Take_AfterTheDisplayChanged_TheRedrawnDraftGoesLiveAtTheNewCanvas()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.Build((1920, 1080, 60, false));
            var rx = Rx();
            var draft = await DraftFor(o, rx, "matrix2x2", (0, "tx1"));
            c.DisplayEdid = TestEdids.ViewSonic4k;
            var changed = await o.TakeAsync(rx, draft);

            var result = await o.TakeAsync(rx, draft.WithLayout(changed.NewPlan!));

            Assert.Equal(TakeOutcome.Applied, result.Outcome);
            Assert.Equal("scaler:tx1:1920x1080", c.MultiViewCalls[0]);
            Assert.Equal("divider:tx1:half", c.MultiViewCalls[1]); // 4K60 matrix is over the bandwidth guideline
            Assert.Equal("layout:avas_rx1_matrix2x2_3840x2160", c.MultiViewCalls[2]);
            Assert.StartsWith("multiview:rx1:avas_rx1_matrix2x2_3840x2160:", c.MultiViewCalls[3]);
            Assert.Equal(3840, o.GetCanvas(rx)!.Width);
        }

        [Fact]
        public async Task Take_LoadsTheLayoutAtTheDisplaysRefreshRate()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.Build((1920, 1080, 30, false));

            await TakeAsync(o, Rx(), "matrix2x2", (0, "tx1"));

            Assert.Equal(30, c.LastFps);
        }

        [Fact]
        public async Task Take_On4kCanvas_ScalesSourcesToFullHdWindows()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;

            await TakeAsync(o, Rx(), "matrix2x2", (1, "tx1"));

            Assert.Equal("scaler:tx1:1920x1080", c.MultiViewCalls[0]);
        }

        // ---- stream 1 frame divider ------------------------------------------------------------

        [Fact]
        public async Task LightLayout_LeavesTheFrameRateAlone()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;

            await TakeAsync(o, Rx(), "side_by_side", (0, "tx1"));

            Assert.DoesNotContain(c.MultiViewCalls, x => x.StartsWith("divider:"));
        }

        [Fact]
        public async Task FullHdLayout_LeavesTheFrameRateAlone()
        {
            var (o, c) = Create();

            await TakeAsync(o, Rx(), "matrix2x2", (0, "tx1"));

            Assert.DoesNotContain(c.MultiViewCalls, x => x.StartsWith("divider:"));
        }

        [Fact]
        public async Task DividerRefused_IsNotFatal_AndTheCardSaysSo()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            c.FailMultiViewPrefix = "divider:";
            var rx = Rx();
            var draft = await DraftFor(o, rx, "matrix2x2");
            draft.Place(0, "tx1", "Camera A");

            var result = await o.TakeAsync(rx, draft);

            Assert.Equal(TakeOutcome.Applied, result.Outcome);
            Assert.Single(o.GetLive(rx)!.Placements);
            string note = o.DescribeCanvas(rx);
            Assert.Contains("Camera A", note);
            Assert.Contains("full rate", note);
        }

        [Fact]
        public async Task ASourceStillUsedByAnotherHeavyLayout_KeepsItsHalvedRate()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            var rx1 = Rx("rx1");
            var rx2 = Rx("rx2");
            await TakeAsync(o, rx1, "matrix2x2", (0, "tx1"));
            await TakeAsync(o, rx2, "matrix2x2", (0, "tx1"));

            await o.TurnOffAsync(rx1);

            Assert.DoesNotContain("divider:tx1:full", c.MultiViewCalls);
        }

        [Fact]
        public async Task TurnOff_RestoresTheFrameRateOfEverySourceItHalved()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            var rx = Rx();
            await TakeAsync(o, rx, "matrix2x2", (0, "tx1"), (1, "tx2"));
            c.MultiViewCalls.Clear();

            await o.TurnOffAsync(rx);

            Assert.Contains("divider:tx1:full", c.MultiViewCalls);
            Assert.Contains("divider:tx2:full", c.MultiViewCalls);
        }

        [Fact]
        public async Task ReturnToSingleView_RestoresTheFrameRate()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            var rx = Rx();
            await TakeAsync(o, rx, "matrix2x2", (0, "tx1"));
            c.MultiViewCalls.Clear();

            await o.ReturnToSingleViewAsync(rx);

            Assert.Contains("divider:tx1:full", c.MultiViewCalls);
        }

        [Fact]
        public async Task ASourceThatWasAlreadyHalvedBeforeWeStarted_IsLeftHalvedOnTheWayOut()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            c.AlreadyHalved.Add("tx1"); // e.g. VEGA_1100, configured that way by the lab
            var rx = Rx();
            await TakeAsync(o, rx, "matrix2x2", (0, "tx1"));
            c.MultiViewCalls.Clear();

            await o.TurnOffAsync(rx);

            Assert.DoesNotContain("divider:tx1:full", c.MultiViewCalls);
        }

        [Fact]
        public async Task SourcesTheToolNeverHalved_AreNeverTouchedOnTheWayOut()
        {
            var (o, c) = Create();
            var rx = Rx();
            await TakeAsync(o, rx, "matrix2x2", (0, "tx1")); // 1080p fallback: no divider needed
            c.MultiViewCalls.Clear();

            await o.TurnOffAsync(rx);

            Assert.DoesNotContain(c.MultiViewCalls, x => x.StartsWith("divider:"));
        }

        [Fact]
        public async Task ARejectedHeavyTake_RestoresTheDividerItSwitchedOn()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            c.FailMultiViewPrefix = "multiview:";

            await TakeAsync(o, Rx(), "matrix2x2", (0, "tx1"));

            Assert.Contains("divider:tx1:half", c.MultiViewCalls);
            Assert.Equal("divider:tx1:full", c.MultiViewCalls.Last()); // nothing live uses it
        }

        // ---- subscriptions --------------------------------------------------------------------------

        [Fact]
        public async Task SecondTake_PreparesEveryEncoderAndReloadsWithEverySubscription()
        {
            var (o, c) = Create();
            var rx = Rx();
            await TakeAsync(o, rx, "matrix2x2", (0, "tx1"));
            c.MultiViewCalls.Clear();

            var result = await TakeAsync(o, rx, "matrix2x2", (0, "tx1"), (3, "tx2"));

            Assert.Equal(TakeOutcome.Applied, result.Outcome);
            Assert.Equal(new[]
            {
                "scaler:tx1:960x540",
                "scaler:tx2:960x540",
                "layout:avas_rx1_matrix2x2_1920x1080", // rewritten every take: the surfaces may change
                "multiview:rx1:avas_rx1_matrix2x2_1920x1080:tx1@1,tx2@2",
            }, c.MultiViewCalls);
            Assert.Equal(2, c.LastSubscriptions!.Count);
        }

        [Fact]
        public async Task OneEncoderInTwoEqualBlocks_IsOneSubscriptionReadByBothWindows()
        {
            var (o, c) = Create();
            var rx = Rx();

            var result = await TakeAsync(o, rx, "matrix2x2", (0, "tx1"), (2, "tx1"));

            Assert.Equal(TakeOutcome.Applied, result.Outcome);
            var sub = Assert.Single(c.LastSubscriptions!);
            Assert.Equal(("tx1", 1), (sub.SourceMac, sub.SubscriptionIndex));
            Assert.Equal(new int?[] { 1, null, 1, null }, c.LastSurfacePlan!.Windows.Select(w => w.Surface));
            Assert.Equal(new[] { 0, 2 }, o.GetLive(rx)!.SlotsShowing("tx1"));
        }

        [Fact]
        public async Task ReplacingTheSourceInABlock_KeepsTheOtherBlocks()
        {
            var (o, c) = Create();
            var rx = Rx();
            await TakeAsync(o, rx, "matrix2x2", (0, "tx1"), (1, "tx2"));

            var result = await TakeAsync(o, rx, "matrix2x2", (0, "tx3"), (1, "tx2"));

            Assert.Equal(TakeOutcome.Applied, result.Outcome);
            var live = o.GetLive(rx)!;
            Assert.Equal("tx3", live.Placements[0].SourceMac);
            Assert.Equal("tx2", live.Placements[1].SourceMac);
            Assert.Equal(2, c.LastSubscriptions!.Count);
        }

        [Fact]
        public async Task SameSourceAtADifferentSizeOnAnotherDecoder_IsRejectedBeforeAnyHardwareCall()
        {
            var (o, c) = Create();
            var rx1 = Rx("rx1");
            var rx2 = Rx("rx2");
            var matrix = await DraftFor(o, rx1, "matrix2x2");
            matrix.Place(0, "tx1", "Camera A"); // 960x540
            await o.TakeAsync(rx1, matrix);
            c.MultiViewCalls.Clear();

            var pip = await DraftFor(o, rx2, "pip");
            pip.Place(pip.Plan.Slots.Single(s => s.Label == "Main").Index, "tx1", "Camera A"); // would need 1920x1080
            var result = await o.TakeAsync(rx2, pip);

            Assert.Equal(TakeOutcome.Rejected, result.Outcome);
            Assert.Contains("Camera A", result.Error);
            Assert.Contains("960x540", result.Error);
            Assert.Contains("RX rx1", result.Error);
            Assert.Empty(c.MultiViewCalls);
            Assert.Null(o.GetLive(rx2));
        }

        [Fact]
        public async Task SameSourceAtTheSameSizeOnTwoDecoders_IsAllowed()
        {
            var (o, _) = Create();

            Assert.Equal(TakeOutcome.Applied, (await TakeAsync(o, Rx("rx1"), "matrix2x2", (0, "tx1"))).Outcome);
            Assert.Equal(TakeOutcome.Applied, (await TakeAsync(o, Rx("rx2"), "matrix2x2", (1, "tx1"))).Outcome);
        }

        [Fact]
        public async Task UsesExcept_ListsTheBlocksOfEveryOtherLiveDisplay()
        {
            var (o, _) = Create();
            var rx1 = Rx("rx1");
            var rx2 = Rx("rx2");
            await TakeAsync(o, rx1, "matrix2x2", (0, "tx1"), (3, "tx1"));
            await TakeAsync(o, rx2, "side_by_side", (0, "tx2"));

            var uses = o.UsesExcept(rx2).ToList();

            Assert.Equal(2, uses.Count);
            Assert.All(uses, u => Assert.Equal(("RX rx1", "tx1", 960, 540), (u.DisplayName, u.SourceMac, u.Width, u.Height)));
        }

        // ---- refusals -------------------------------------------------------------------------------

        [Fact]
        public async Task Take_EmptyDraft_IsRejectedWithoutAnyCall()
        {
            var (o, c) = Create();
            var rx = Rx();
            var draft = await DraftFor(o, rx, "matrix2x2");
            int reads = c.EdidReads;

            var result = await o.TakeAsync(rx, draft);

            Assert.Equal(TakeOutcome.Rejected, result.Outcome);
            Assert.Equal("Place at least one source (Step 2).", result.Error);
            Assert.Empty(c.MultiViewCalls);
            Assert.Equal(reads, c.EdidReads);
        }

        [Fact]
        public async Task ScalerFailure_AbortsWithoutCreatingOrLoadingTheLayout()
        {
            var (o, c) = Create();
            var rx = Rx();
            c.FailMultiViewPrefix = "scaler:";

            var result = await TakeAsync(o, rx, "matrix2x2", (0, "tx1"));

            Assert.Equal(TakeOutcome.Rejected, result.Outcome);
            Assert.Single(c.MultiViewCalls);
            Assert.Null(o.GetLive(rx));
        }

        [Fact]
        public async Task LayoutCreateFailure_DoesNotLoadAnything()
        {
            var (o, c) = Create();
            var rx = Rx();
            c.FailMultiViewPrefix = "layout:";

            var result = await TakeAsync(o, rx, "matrix2x2", (0, "tx1"));

            Assert.Equal(TakeOutcome.Rejected, result.Outcome);
            Assert.DoesNotContain(c.MultiViewCalls, x => x.StartsWith("multiview:"));
            Assert.False(o.IsMultiView(rx));
        }

        // ---- leaving multiview ----------------------------------------------------------------------

        [Fact]
        public async Task ReturnToSingleView_SetsGenlockAndForgetsTheLayout()
        {
            var (o, c) = Create();
            var rx = Rx();
            await TakeAsync(o, rx, "matrix2x2", (0, "tx1"));
            await TakeAsync(o, rx, "matrix2x2", (0, "tx2"));
            c.MultiViewCalls.Clear();

            bool ok = await o.ReturnToSingleViewAsync(rx);

            Assert.True(ok);
            Assert.Equal(new[] { "genlock:rx1" }, c.MultiViewCalls);
            Assert.False(o.IsMultiView(rx));
            Assert.Null(o.GetLive(rx));
            Assert.False(o.CanUndo(rx));
        }

        [Fact]
        public async Task ReturnToSingleView_WhenNotInMultiView_DoesNothing()
        {
            var (o, c) = Create();

            bool ok = await o.ReturnToSingleViewAsync(Rx());

            Assert.True(ok);
            Assert.Empty(c.MultiViewCalls);
        }

        [Fact]
        public async Task TurnOff_LeavesEachSubscriptionOnceThenGenlocksAndForgetsTheLayout()
        {
            var (o, c) = Create();
            var rx = Rx();
            await TakeAsync(o, rx, "matrix2x2", (0, "tx1"), (1, "tx2"), (3, "tx1"));
            c.MultiViewCalls.Clear();

            bool ok = await o.TurnOffAsync(rx);

            Assert.True(ok);
            Assert.Equal(new[] { "rx1:1", "rx1:2" }, c.Leaves);
            Assert.Equal(new[] { "genlock:rx1" }, c.MultiViewCalls);
            Assert.False(o.IsMultiView(rx));
        }

        [Fact]
        public async Task ATakeThatShrinks_LeavesTheSubscriptionThatDroppedOut_AndOffLeavesTheRest()
        {
            var (o, c) = Create();
            var rx = Rx();
            await TakeAsync(o, rx, "matrix2x2", (0, "tx1"), (1, "tx2")); // tx1@1, tx2@2

            await TakeAsync(o, rx, "matrix2x2", (0, "tx1"), (1, "tx1")); // tx1@1 only

            Assert.Equal(new[] { "rx1:2" }, c.Leaves);
            await o.TurnOffAsync(rx);
            Assert.Equal(new[] { "rx1:2", "rx1:1" }, c.Leaves); // every index that was joined has been left
        }

        [Fact]
        public async Task AFailedTake_LeavesNothing_AndOffStillLeavesWhatIsJoined()
        {
            var (o, c) = Create();
            var rx = Rx();
            await TakeAsync(o, rx, "matrix2x2", (0, "tx1"), (1, "tx2"));
            c.FailMultiViewPrefix = "multiview:";

            var result = await TakeAsync(o, rx, "matrix2x2", (0, "tx3"));

            Assert.Equal(TakeOutcome.Rejected, result.Outcome);
            Assert.Empty(c.Leaves);
            c.FailMultiViewPrefix = null;
            await o.TurnOffAsync(rx);
            Assert.Equal(new[] { "rx1:1", "rx1:2" }, c.Leaves);
        }

        [Fact]
        public async Task Undo_LeavesTheSubscriptionsTheRestoredLayoutDoesNotUse()
        {
            var (o, c) = Create();
            var rx = Rx();
            await TakeAsync(o, rx, "matrix2x2", (0, "tx1"));
            await TakeAsync(o, rx, "matrix2x2", (0, "tx1"), (1, "tx2"));

            await o.UndoAsync(rx);

            Assert.Equal(new[] { "rx1:2" }, c.Leaves);
        }

        [Fact]
        public async Task MacsAreComparedCaseAndSeparatorInsensitively()
        {
            var (o, _) = Create();

            await TakeAsync(o, Rx("74:FE:48:3C:05:9E"), "matrix2x2", (0, "tx1"));

            Assert.True(o.IsMultiView(Rx("74fe483c059e")));
        }
    }
}
