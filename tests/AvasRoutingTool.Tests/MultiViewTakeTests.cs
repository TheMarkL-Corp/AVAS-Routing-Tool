using System.Linq;
using System.Threading.Tasks;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>A Take sends a whole staged draft at once; Undo takes the previous one again.</summary>
    public class MultiViewTakeTests
    {
        private static AvasDevice Rx(string mac = "rx1") =>
            new() { DeviceId = mac, DeviceName = "RX " + mac, IsReceiver = true, IsActive = true };

        private static (MultiViewOrchestrator o, MockSdvoeClient c) Create()
        {
            var c = new MockSdvoeClient();
            return (new MultiViewOrchestrator(c), c);
        }

        private static MultiViewDraft Draft(MultiViewLayoutPlan plan, params (int Slot, string Mac)[] placements)
        {
            var draft = new MultiViewDraft(plan);
            foreach (var (slot, mac) in placements)
                Assert.True(draft.Place(slot, mac, mac.ToUpperInvariant()).Ok);
            return draft;
        }

        private static string[] Blocks(MultiViewDraft? draft) =>
            draft == null
                ? System.Array.Empty<string>()
                : draft.Placements.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.SourceMac}").ToArray();

        [Fact]
        public async Task Take_FirstTime_ScalesEachDistinctEncoderOnce_CreatesTheLayout_LoadsItOnce()
        {
            var (o, c) = Create();
            var rx = Rx();
            var (plan, _) = await o.PlanForDisplayAsync(rx, "matrix2x2"); // no EDID: 1920x1080 fallback

            var result = await o.TakeAsync(rx, Draft(plan, (0, "tx2"), (3, "tx2"), (1, "tx1")));

            Assert.Equal(TakeOutcome.Applied, result.Outcome);
            Assert.Null(result.Error);
            Assert.Equal(new[]
            {
                "scaler:tx2:960x540",
                "scaler:tx1:960x540",
                "layout:avas_rx1_matrix2x2_1920x1080",
                "multiview:rx1:avas_rx1_matrix2x2_1920x1080:tx2@1,tx1@2",
            }, c.MultiViewCalls);
            Assert.Equal(new int?[] { 1, 2, null, 1 }, c.LastSurfacePlan!.Windows.Select(w => w.Surface)); // block 3 is black
            Assert.True(o.IsMultiView(rx));
            Assert.Equal(new[] { "0:tx2", "1:tx1", "3:tx2" }, Blocks(o.GetLive(rx)));
        }

        [Fact]
        public async Task Take_ReReadsTheEdid_AndOnAChangedDisplayChangesNothing()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.Build((1920, 1080, 60, false));
            var rx = Rx();
            var (plan, _) = await o.PlanForDisplayAsync(rx, "matrix2x2");

            c.DisplayEdid = TestEdids.ViewSonic4k; // a 4K panel was plugged in meanwhile
            var result = await o.TakeAsync(rx, Draft(plan, (0, "tx1")));

            Assert.Equal(TakeOutcome.DisplayChanged, result.Outcome);
            Assert.Equal(2, c.EdidReads);
            Assert.Empty(c.MultiViewCalls);
            Assert.Equal(("matrix2x2", 3840, 2160), (result.NewPlan!.Id, result.NewPlan.Width, result.NewPlan.Height));
            Assert.Null(o.GetLive(rx));
            Assert.False(o.IsMultiView(rx));
        }

        [Fact]
        public async Task Take_HeavyLayout_HalvesEachDistinctEncoderOnce()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k; // 4K60 matrix, ~11.9 Gbps with every window live
            var rx = Rx();
            var (plan, _) = await o.PlanForDisplayAsync(rx, "matrix2x2");

            var result = await o.TakeAsync(rx, Draft(plan, (0, "tx1"), (1, "tx1"), (2, "tx2")));

            Assert.Equal(TakeOutcome.Applied, result.Outcome);
            Assert.Equal(new[]
            {
                "scaler:tx1:1920x1080",
                "divider:tx1:half",
                "scaler:tx2:1920x1080",
                "divider:tx2:half",
                "layout:avas_rx1_matrix2x2_3840x2160",
                "multiview:rx1:avas_rx1_matrix2x2_3840x2160:tx1@1,tx2@2",
            }, c.MultiViewCalls);
            Assert.Contains("half frame rate", o.DescribeCanvas(rx));
        }

        [Fact]
        public async Task Take_Rejected_KeepsTheOldLiveState()
        {
            var (o, c) = Create();
            var rx = Rx();
            var (plan, _) = await o.PlanForDisplayAsync(rx, "matrix2x2");
            await o.TakeAsync(rx, Draft(plan, (0, "tx1")));
            c.FailMultiViewPrefix = "multiview:";

            var result = await o.TakeAsync(rx, Draft(plan, (0, "tx1"), (1, "tx2")));

            Assert.Equal(TakeOutcome.Rejected, result.Outcome);
            Assert.Contains("RX rx1", result.Error);
            Assert.Equal(new[] { "0:tx1" }, Blocks(o.GetLive(rx)));
            Assert.False(o.CanUndo(rx)); // the refused take is not an undo step
        }

        [Fact]
        public async Task Take_SameAsLive_IsNothingToTake()
        {
            var (o, c) = Create();
            var rx = Rx();
            var (plan, _) = await o.PlanForDisplayAsync(rx, "matrix2x2");
            var draft = Draft(plan, (0, "tx1"), (2, "tx2"));
            await o.TakeAsync(rx, draft);
            c.MultiViewCalls.Clear();
            int reads = c.EdidReads;

            var result = await o.TakeAsync(rx, draft.Clone());

            Assert.Equal(TakeOutcome.NothingToTake, result.Outcome);
            Assert.Empty(c.MultiViewCalls);
            Assert.Equal(reads, c.EdidReads);
        }

        [Fact]
        public async Task Undo_TakesThePreviousLiveDraftAgain()
        {
            var (o, c) = Create();
            var rx = Rx();
            var (plan, _) = await o.PlanForDisplayAsync(rx, "matrix2x2");
            await o.TakeAsync(rx, Draft(plan, (0, "tx1")));
            await o.TakeAsync(rx, Draft(plan, (0, "tx2"), (1, "tx3")));
            Assert.True(o.CanUndo(rx));
            c.MultiViewCalls.Clear();

            var result = await o.UndoAsync(rx);

            Assert.Equal(TakeOutcome.Applied, result.Outcome);
            Assert.Equal(new[] { "0:tx1" }, Blocks(o.GetLive(rx)));
            Assert.Equal("multiview:rx1:avas_rx1_matrix2x2_1920x1080:tx1@1", c.MultiViewCalls.Last());
            Assert.False(o.CanUndo(rx)); // single level
            Assert.Equal(TakeOutcome.NothingToTake, (await o.UndoAsync(rx)).Outcome);
        }

        [Fact]
        public async Task Take_EncoderLeavingEveryHeavyLayout_GetsItsFrameRateRestored()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            var rx = Rx();
            var (plan, _) = await o.PlanForDisplayAsync(rx, "matrix2x2");
            await o.TakeAsync(rx, Draft(plan, (0, "tx1"), (1, "tx2")));
            c.MultiViewCalls.Clear();

            await o.TakeAsync(rx, Draft(plan, (0, "tx3"), (1, "tx2")));

            Assert.Contains("divider:tx3:half", c.MultiViewCalls);
            Assert.Contains("divider:tx1:full", c.MultiViewCalls);
            Assert.DoesNotContain("divider:tx2:full", c.MultiViewCalls);
            // restored only once the new layout is live
            Assert.True(c.MultiViewCalls.IndexOf("divider:tx1:full") > c.MultiViewCalls.FindIndex(x => x.StartsWith("multiview:")));
        }

        [Fact]
        public void Restore_SharedSurfaceFixture_GivesADraftWithTheEncoderInBothBlocks()
        {
            var state = RxStateReader.Parse(Fixtures.Read("rx_multiview_shared_surface.json"), Fixtures.Read("rx_multiview_shared_surface_layout.json"))!;
            // The spike used its own layout name; the windows are what this tool now creates.
            state = state with { Layout = state.Layout! with { Name = "avas_74fe483c059e_matrix2x2_1920x1080" } };

            var restored = MultiViewRestore.Analyze(state)!;

            Assert.True(restored.IsRecognised);
            var draft = restored.Draft!;
            var block1 = draft.Placements[0];
            var block3 = draft.Placements[2];
            Assert.Equal(SdvoeClient.NormalizeMac(block1.SourceMac), SdvoeClient.NormalizeMac(block3.SourceMac));
            Assert.Equal(block1.SourceName, block3.SourceName);
            Assert.False(draft.Placements.ContainsKey(3)); // surface 255: a black window
            Assert.Equal(new[] { 0, 2 }, restored.Sources.Where(s => s.Mac == block1.SourceMac).Select(s => s.SlotIndex));
        }

        [Fact]
        public void Restore_LegacyLayoutNames_StillAdopt()
        {
            var (o, c) = Create();
            var rx = Rx("74fe483c059e");
            var state = RxStateReader.Parse(Fixtures.Read("rx_multiview.json"), Fixtures.Read("rx_multiview_layout.json"))!;
            Assert.Equal("avas_matrix2x2_1920x1080", state.Layout!.Name); // the name before per-display layouts

            var restored = MultiViewRestore.Analyze(state)!;
            o.Adopt(rx, restored.Draft!, restored.Canvas, restored.SubscriptionIndexes);

            Assert.True(o.IsMultiView(rx));
            Assert.Equal(new[] { "0:74fe488b07bb", "1:0016c0589c1e" }, Blocks(o.GetLive(rx)));
            Assert.Empty(c.MultiViewCalls);
            Assert.Equal(0, c.EdidReads);
        }
    }
}
