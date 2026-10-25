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
        private static AvasDevice Tx(string mac, string name = "TX") =>
            new() { DeviceId = mac, DeviceName = name, IsTransmitter = true, IsActive = true };

        private static AvasDevice Rx(string mac = "rx1") =>
            new() { DeviceId = mac, DeviceName = "RX " + mac, IsReceiver = true, IsActive = true };

        private static (MultiViewOrchestrator o, MockSdvoeClient c) Create()
        {
            var c = new MockSdvoeClient();
            return (new MultiViewOrchestrator(c), c);
        }

        private static MultiViewCanvas Canvas(int w, int h, int fps) => new(w, h, fps, false, $"{w}x{h}@{fps} (running)");

        [Fact]
        public void Adopt_RegistersTheSessionWithoutTouchingAnyDevice()
        {
            var (o, c) = Create();
            var rx = Rx();
            var plan = MultiViewPresets.Build("matrix2x2", 1920, 1080);

            o.Adopt(rx, plan, Canvas(1920, 1080, 60), new Dictionary<int, AvasDevice> { [0] = Tx("tx1") });

            Assert.True(o.IsMultiView(rx));
            Assert.Equal("tx1", o.GetAssignments(rx)[0].DeviceId);
            Assert.Empty(c.MultiViewCalls);
            Assert.Equal(0, c.EdidReads);
        }

        [Fact]
        public async Task AssignAfterAdopt_ReloadsWithoutRecreatingTheLayoutOrRereadingTheEdid()
        {
            var (o, c) = Create();
            var rx = Rx();
            var plan = MultiViewPresets.Build("matrix2x2", 1920, 1080);
            o.Adopt(rx, plan, Canvas(1920, 1080, 60), new Dictionary<int, AvasDevice> { [0] = Tx("tx1") });

            var result = await o.AssignAsync(rx, 1, Tx("tx2"));

            Assert.True(result.Success);
            Assert.DoesNotContain(c.MultiViewCalls, x => x.StartsWith("layout:"));
            Assert.Equal(0, c.EdidReads);
            Assert.Equal(2, c.LastSubscriptions!.Count); // the restored source stays
        }

        [Fact]
        public async Task AdoptedHeavyLayout_NeverRestoresDividersItDidNotSet()
        {
            var (o, c) = Create();
            var rx = Rx();
            var plan = MultiViewPresets.Build("matrix2x2", 3840, 2160); // heavy at 60 fps
            o.Adopt(rx, plan, Canvas(3840, 2160, 60), new Dictionary<int, AvasDevice> { [0] = Tx("tx1") });

            await o.TurnOffAsync(rx);

            Assert.DoesNotContain(c.MultiViewCalls, x => x.StartsWith("divider:"));
        }

        [Fact]
        public async Task AdoptedSession_CanReturnToSingleView()
        {
            var (o, c) = Create();
            var rx = Rx();
            o.Adopt(rx, MultiViewPresets.Build("matrix2x2", 1920, 1080), Canvas(1920, 1080, 60), new Dictionary<int, AvasDevice>());

            Assert.True(await o.ReturnToSingleViewAsync(rx));

            Assert.Equal(new[] { "genlock:rx1" }, c.MultiViewCalls);
            Assert.False(o.IsMultiView(rx));
        }

        [Fact]
        public async Task ExitExternal_LeavesTheGivenSubscriptionsThenGenlocks()
        {
            var (o, c) = Create();
            var rx = Rx();

            bool ok = await o.ExitExternalAsync(rx, new[] { 1, 2 });

            Assert.True(ok);
            Assert.Equal("rx1", c.LastUnroutedRx);
            Assert.Equal(new[] { "genlock:rx1" }, c.MultiViewCalls);
        }

        [Fact]
        public async Task ExitExternal_WithNothingToLeave_JustGenlocks()
        {
            var (o, c) = Create();

            Assert.True(await o.ExitExternalAsync(Rx(), System.Array.Empty<int>()));

            Assert.Null(c.LastUnroutedRx);
            Assert.Equal(new[] { "genlock:rx1" }, c.MultiViewCalls);
        }

        [Fact]
        public async Task ExitExternal_GenlockRefused_ReportsFailure()
        {
            var (o, c) = Create();
            c.FailMultiViewPrefix = "genlock:";

            Assert.False(await o.ExitExternalAsync(Rx(), new[] { 1 }));
        }
    }
}
