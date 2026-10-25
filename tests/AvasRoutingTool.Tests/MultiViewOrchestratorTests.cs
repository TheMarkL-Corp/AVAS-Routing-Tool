using System.Linq;
using System.Threading.Tasks;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class MultiViewOrchestratorTests
    {
        private static AvasDevice Tx(string mac, string name = "TX", bool active = true) =>
            new() { DeviceId = mac, DeviceName = name, IsTransmitter = true, IsActive = active };

        private static AvasDevice Rx(string mac = "rx1") =>
            new() { DeviceId = mac, DeviceName = "RX " + mac, IsReceiver = true, IsActive = true };

        private static (MultiViewOrchestrator o, MockSdvoeClient c) Create()
        {
            var c = new MockSdvoeClient();
            return (new MultiViewOrchestrator(c), c);
        }

        [Fact]
        public async Task BeginLayout_ReturnsPlanAndDoesNotChangeAnyDevice()
        {
            var (o, c) = Create();
            var rx = Rx();

            var plan = await o.BeginLayoutAsync(rx, "matrix2x2");

            Assert.Equal("avas_matrix2x2_1920x1080", plan.Name);
            Assert.True(o.IsMultiView(rx));
            Assert.Empty(o.GetAssignments(rx));
            Assert.Empty(c.MultiViewCalls);
        }

        [Fact]
        public async Task BeginLayout_ReadsTheDisplayEdidAndSizesTheCanvasToIt()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            var rx = Rx();

            var plan = await o.BeginLayoutAsync(rx, "matrix2x2");

            Assert.Equal(1, c.EdidReads);
            Assert.Equal("avas_matrix2x2_3840x2160", plan.Name);
            Assert.Equal((1920, 1080), (plan.Slots[0].Width, plan.Slots[0].Height));
            var canvas = o.GetCanvas(rx)!;
            Assert.True(canvas.FromDisplay);
            Assert.Equal((3840, 2160, 60), (canvas.Width, canvas.Height, canvas.Fps));
            Assert.Contains("VP3268-4K", o.DescribeCanvas(rx));
        }

        [Fact]
        public async Task BeginLayout_WithoutAnEdid_FallsBackToFullHd()
        {
            var (o, c) = Create();
            c.DisplayEdid = null;
            var rx = Rx();

            var plan = await o.BeginLayoutAsync(rx, "matrix2x2");

            Assert.Equal("avas_matrix2x2_1920x1080", plan.Name);
            Assert.False(o.GetCanvas(rx)!.FromDisplay);
            Assert.Contains("EDID unavailable", o.DescribeCanvas(rx));
        }

        [Fact]
        public async Task FirstAssign_ReadsTheEdidAgainAndAdoptsADifferentDisplay()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.Build((1920, 1080, 60, false));
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2");

            c.DisplayEdid = TestEdids.ViewSonic4k; // a 4K panel was plugged in meanwhile
            var result = await o.AssignAsync(rx, 0, Tx("tx1"));

            Assert.True(result.Success);
            Assert.True(result.PlanChanged);
            Assert.Equal("avas_matrix2x2_3840x2160", result.Plan!.Name);
            Assert.Equal(2, c.EdidReads);
            Assert.Equal("scaler:tx1:1920x1080", c.MultiViewCalls[0]);
            Assert.Equal("divider:tx1:half", c.MultiViewCalls[1]); // 4K60 matrix is over the bandwidth guideline
            Assert.Equal("layout:avas_matrix2x2_3840x2160", c.MultiViewCalls[2]);
            Assert.StartsWith("multiview:rx1:avas_matrix2x2_3840x2160:", c.MultiViewCalls[3]);
        }

        [Fact]
        public async Task FirstAssign_WhenTheDisplayIsUnchanged_ReportsNoPlanChange()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2");

            var result = await o.AssignAsync(rx, 0, Tx("tx1"));

            Assert.False(result.PlanChanged);
            Assert.Equal(2, c.EdidReads); // once to pick the layout, once before touching hardware
        }

        [Fact]
        public async Task LaterAssigns_DoNotReadTheEdidAgain()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2");
            await o.AssignAsync(rx, 0, Tx("tx1"));
            int reads = c.EdidReads;

            await o.AssignAsync(rx, 1, Tx("tx2"));

            Assert.Equal(reads, c.EdidReads);
        }

        [Fact]
        public async Task Assign_LoadsTheLayoutAtTheDisplaysRefreshRate()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.Build((1920, 1080, 30, false));
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2");

            await o.AssignAsync(rx, 0, Tx("tx1"));

            Assert.Equal(30, c.LastFps);
        }

        [Fact]
        public async Task FourKCanvas_ScalesSourcesToFullHdWindows()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2");

            await o.AssignAsync(rx, 1, Tx("tx1"));

            Assert.Equal("scaler:tx1:1920x1080", c.MultiViewCalls[0]);
        }

        // ---- stream 1 frame divider ------------------------------------------------------------

        [Fact]
        public async Task HeavyLayout_HalvesTheSourcesFrameRateBetweenScalingAndLoading()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k; // 4K60 matrix, ~11.9 Gbps with every window live
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2");

            var result = await o.AssignAsync(rx, 0, Tx("tx1"));

            Assert.True(result.Success);
            Assert.Equal("scaler:tx1:1920x1080", c.MultiViewCalls[0]);
            Assert.Equal("divider:tx1:half", c.MultiViewCalls[1]);
            Assert.Equal("layout:avas_matrix2x2_3840x2160", c.MultiViewCalls[2]);
            Assert.StartsWith("multiview:rx1:", c.MultiViewCalls[3]);
            Assert.Contains("half frame rate", o.DescribeCanvas(rx));
        }

        [Fact]
        public async Task LightLayout_LeavesTheFrameRateAlone()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "side_by_side");

            await o.AssignAsync(rx, 0, Tx("tx1"));

            Assert.DoesNotContain(c.MultiViewCalls, x => x.StartsWith("divider:"));
        }

        [Fact]
        public async Task FullHdLayout_LeavesTheFrameRateAlone()
        {
            var (o, c) = Create();
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2");

            await o.AssignAsync(rx, 0, Tx("tx1"));

            Assert.DoesNotContain(c.MultiViewCalls, x => x.StartsWith("divider:"));
        }

        [Fact]
        public async Task EverySourceInAHeavyLayoutIsHalved()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2");
            await o.AssignAsync(rx, 0, Tx("tx1"));

            await o.AssignAsync(rx, 1, Tx("tx2"));

            Assert.Contains("divider:tx2:half", c.MultiViewCalls);
        }

        [Fact]
        public async Task DividerRefused_IsNotFatal_AndTheCardSaysSo()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            c.FailMultiViewPrefix = "divider:";
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2");

            var result = await o.AssignAsync(rx, 0, Tx("tx1", "Camera A"));

            Assert.True(result.Success);
            Assert.Single(o.GetAssignments(rx));
            string note = o.DescribeCanvas(rx);
            Assert.Contains("Camera A", note);
            Assert.Contains("full rate", note);
        }

        [Fact]
        public async Task ReplacingTheSource_RestoresTheFrameRateOfTheOneThatLeft()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2");
            await o.AssignAsync(rx, 0, Tx("tx1"));

            await o.AssignAsync(rx, 0, Tx("tx3"));

            Assert.Contains("divider:tx3:half", c.MultiViewCalls);
            Assert.Contains("divider:tx1:full", c.MultiViewCalls);
        }

        [Fact]
        public async Task ASourceStillUsedByAnotherHeavyLayout_KeepsItsHalvedRate()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            var rx1 = Rx("rx1");
            var rx2 = Rx("rx2");
            await o.BeginLayoutAsync(rx1, "matrix2x2");
            await o.BeginLayoutAsync(rx2, "matrix2x2");
            var tx = Tx("tx1");
            await o.AssignAsync(rx1, 0, tx);
            await o.AssignAsync(rx2, 0, tx);

            await o.TurnOffAsync(rx1);

            Assert.DoesNotContain("divider:tx1:full", c.MultiViewCalls);
        }

        [Fact]
        public async Task TurnOff_RestoresTheFrameRateOfEverySourceItHalved()
        {
            var (o, c) = Create();
            c.DisplayEdid = TestEdids.ViewSonic4k;
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2");
            await o.AssignAsync(rx, 0, Tx("tx1"));
            await o.AssignAsync(rx, 1, Tx("tx2"));
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
            await o.BeginLayoutAsync(rx, "matrix2x2");
            await o.AssignAsync(rx, 0, Tx("tx1"));
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
            await o.BeginLayoutAsync(rx, "matrix2x2");
            await o.AssignAsync(rx, 0, Tx("tx1"));
            c.MultiViewCalls.Clear();

            await o.TurnOffAsync(rx);

            Assert.DoesNotContain("divider:tx1:full", c.MultiViewCalls);
        }

        [Fact]
        public async Task SourcesTheToolNeverHalved_AreNeverTouchedOnTheWayOut()
        {
            var (o, c) = Create();
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2"); // 1080p fallback: no divider needed
            await o.AssignAsync(rx, 0, Tx("tx1"));
            c.MultiViewCalls.Clear();

            await o.TurnOffAsync(rx);

            Assert.DoesNotContain(c.MultiViewCalls, x => x.StartsWith("divider:"));
        }

        [Fact]
        public async Task FirstAssign_ScalesSourceCreatesLayoutThenLoadsItWithTheSubscription()
        {
            var (o, c) = Create();
            var rx = Rx();
            var plan = await o.BeginLayoutAsync(rx, "matrix2x2");
            var slot = plan.Slots[1];

            var result = await o.AssignAsync(rx, slot.Index, Tx("tx1"));

            Assert.True(result.Success);
            Assert.Equal(3, c.MultiViewCalls.Count);
            Assert.Equal("scaler:tx1:960x540", c.MultiViewCalls[0]);
            Assert.Equal("layout:avas_matrix2x2_1920x1080", c.MultiViewCalls[1]);
            Assert.Equal($"multiview:rx1:avas_matrix2x2_1920x1080:tx1@{slot.TargetSurface}", c.MultiViewCalls[2]);
            Assert.Equal("tx1", o.GetAssignments(rx)[slot.Index].DeviceId);
        }

        [Fact]
        public async Task SecondAssign_OnlyPreparesNewSourceAndReloadsWithAllSubscriptions()
        {
            var (o, c) = Create();
            var rx = Rx();
            var plan = await o.BeginLayoutAsync(rx, "matrix2x2");
            await o.AssignAsync(rx, 0, Tx("tx1"));
            c.MultiViewCalls.Clear();

            var result = await o.AssignAsync(rx, 3, Tx("tx2"));

            Assert.True(result.Success);
            Assert.Equal(2, c.MultiViewCalls.Count);
            Assert.Equal("scaler:tx2:960x540", c.MultiViewCalls[0]);
            Assert.StartsWith("multiview:rx1:", c.MultiViewCalls[1]);
            Assert.Equal(2, c.LastSubscriptions!.Count);
            Assert.Equal(plan.Slots[0].TargetSurface, c.LastSubscriptions.Single(s => s.SourceMac == "tx1").SubscriptionIndex);
            Assert.Equal(plan.Slots[3].TargetSurface, c.LastSubscriptions.Single(s => s.SourceMac == "tx2").SubscriptionIndex);
        }

        [Fact]
        public async Task AssigningASourceAlreadyInTheLayout_MovesItBecauseDuplicateStreamsAreIllegal()
        {
            var (o, c) = Create();
            var rx = Rx();
            var plan = await o.BeginLayoutAsync(rx, "matrix2x2");
            var tx = Tx("tx1");
            await o.AssignAsync(rx, 0, tx);

            var result = await o.AssignAsync(rx, 2, tx);

            Assert.True(result.Success);
            Assert.Equal(new[] { 0 }, result.ClearedSlots);
            var assignments = o.GetAssignments(rx);
            Assert.False(assignments.ContainsKey(0));
            Assert.Equal("tx1", assignments[2].DeviceId);
            Assert.Single(c.LastSubscriptions!);
            Assert.Equal(plan.Slots[2].TargetSurface, c.LastSubscriptions![0].SubscriptionIndex);
        }

        [Fact]
        public async Task ReplacingTheSourceInASlot_KeepsOtherSlots()
        {
            var (o, c) = Create();
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2");
            await o.AssignAsync(rx, 0, Tx("tx1"));
            await o.AssignAsync(rx, 1, Tx("tx2"));

            var result = await o.AssignAsync(rx, 0, Tx("tx3"));

            Assert.True(result.Success);
            var assignments = o.GetAssignments(rx);
            Assert.Equal("tx3", assignments[0].DeviceId);
            Assert.Equal("tx2", assignments[1].DeviceId);
            Assert.Equal(2, c.LastSubscriptions!.Count);
        }

        [Fact]
        public async Task SameSourceAtADifferentSizeOnAnotherDecoder_IsRejectedBeforeAnyHardwareCall()
        {
            var (o, c) = Create();
            var rx1 = Rx("rx1");
            var rx2 = Rx("rx2");
            await o.BeginLayoutAsync(rx1, "matrix2x2");
            await o.BeginLayoutAsync(rx2, "pip");
            var tx = Tx("tx1", "Camera A");
            await o.AssignAsync(rx1, 0, tx); // 960x540
            c.MultiViewCalls.Clear();

            var pipMain = o.GetPlan(rx2)!.Slots.Single(s => s.Label == "Main");
            var result = await o.AssignAsync(rx2, pipMain.Index, tx); // would need 1920x1080

            Assert.False(result.Success);
            Assert.Contains("Camera A", result.Error);
            Assert.Contains("960x540", result.Error);
            Assert.Empty(c.MultiViewCalls);
            Assert.Empty(o.GetAssignments(rx2));
        }

        [Fact]
        public async Task SameSourceAtTheSameSizeOnTwoDecoders_IsAllowed()
        {
            var (o, _) = Create();
            var rx1 = Rx("rx1");
            var rx2 = Rx("rx2");
            await o.BeginLayoutAsync(rx1, "matrix2x2");
            await o.BeginLayoutAsync(rx2, "matrix2x2");
            var tx = Tx("tx1");

            Assert.True((await o.AssignAsync(rx1, 0, tx)).Success);
            Assert.True((await o.AssignAsync(rx2, 1, tx)).Success);
        }

        [Fact]
        public async Task ScalerFailure_AbortsWithoutCreatingOrLoadingTheLayout()
        {
            var (o, c) = Create();
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2");
            c.FailMultiViewPrefix = "scaler:";

            var result = await o.AssignAsync(rx, 0, Tx("tx1"));

            Assert.False(result.Success);
            Assert.Single(c.MultiViewCalls);
            Assert.Empty(o.GetAssignments(rx));
        }

        [Fact]
        public async Task LoadFailure_RollsTheAssignmentBack()
        {
            var (o, c) = Create();
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2");
            await o.AssignAsync(rx, 0, Tx("tx1"));
            c.FailMultiViewPrefix = "multiview:";

            var result = await o.AssignAsync(rx, 1, Tx("tx2"));

            Assert.False(result.Success);
            var assignments = o.GetAssignments(rx);
            Assert.Single(assignments);
            Assert.True(assignments.ContainsKey(0));
        }

        [Fact]
        public async Task OfflineOrNonTransmitterSource_IsRejected()
        {
            var (o, c) = Create();
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2");

            var offline = await o.AssignAsync(rx, 0, Tx("tx1", active: false));
            var notTx = await o.AssignAsync(rx, 0, Rx("rx9"));

            Assert.False(offline.Success);
            Assert.False(notTx.Success);
            Assert.Empty(c.MultiViewCalls);
        }

        [Fact]
        public async Task UnknownSlotOrMissingLayout_IsRejected()
        {
            var (o, c) = Create();
            var rx = Rx();

            Assert.False((await o.AssignAsync(rx, 0, Tx("tx1"))).Success);

            await o.BeginLayoutAsync(rx, "side_by_side");
            Assert.False((await o.AssignAsync(rx, 7, Tx("tx1"))).Success);
            Assert.Empty(c.MultiViewCalls);
        }

        [Fact]
        public async Task ReturnToSingleView_SetsGenlockAndForgetsTheLayout()
        {
            var (o, c) = Create();
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2");
            await o.AssignAsync(rx, 0, Tx("tx1"));
            c.MultiViewCalls.Clear();

            bool ok = await o.ReturnToSingleViewAsync(rx);

            Assert.True(ok);
            Assert.Equal(new[] { "genlock:rx1" }, c.MultiViewCalls);
            Assert.False(o.IsMultiView(rx));
            Assert.Empty(o.GetAssignments(rx));
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
        public async Task TurnOff_LeavesEverySubscriptionThenGenlocksAndForgetsTheLayout()
        {
            var (o, c) = Create();
            var rx = Rx();
            await o.BeginLayoutAsync(rx, "matrix2x2");
            await o.AssignAsync(rx, 0, Tx("tx1"));
            await o.AssignAsync(rx, 1, Tx("tx2"));
            c.MultiViewCalls.Clear();

            bool ok = await o.TurnOffAsync(rx);

            Assert.True(ok);
            Assert.Equal("rx1", c.LastUnroutedRx);
            Assert.Equal(new[] { "genlock:rx1" }, c.MultiViewCalls);
            Assert.False(o.IsMultiView(rx));
        }

        [Fact]
        public async Task MacsAreComparedCaseAndSeparatorInsensitively()
        {
            var (o, _) = Create();
            var rx = Rx("74:FE:48:3C:05:9E");
            await o.BeginLayoutAsync(rx, "matrix2x2");

            Assert.True(o.IsMultiView(Rx("74fe483c059e")));
        }
    }
}
