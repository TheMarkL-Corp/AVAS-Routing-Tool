using System.Threading.Tasks;
using AvasRoutingTool.Sdvoe;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class MockSdvoeClient : ISdvoeClient
    {
        public string? LastVideoRx;
        public string? LastVideoTx;
        public string? LastUnroutedRx;
        public bool IsConnected { get; set; } = true;

        public event System.Action<string, bool>? DeviceStateChanged;

        /// <summary>Raises <see cref="DeviceStateChanged"/> as the real client does from its read loop.</summary>
        public void RaiseDeviceStateChanged(string mac, bool online) => DeviceStateChanged?.Invoke(mac, online);

        /// <summary>What ConnectAsync reports when no <see cref="ConnectGate"/> is set (false = the control server is unreachable).</summary>
        public bool ConnectResult { get; set; } = true;

        /// <summary>When set, ConnectAsync waits for this task (the connect call is pending until it completes).</summary>
        public Task<bool>? ConnectGate { get; set; }

        public Task<bool> ConnectAsync(string host, int telnetPort = 6970, int restPort = 8080)
        {
            return ConnectGate ?? Task.FromResult(ConnectResult);
        }

        public Task DisconnectAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>When true, every join is refused (nothing is recorded).</summary>
        public bool FailJoin { get; set; }

        public Task<bool> JoinStreamAsync(string rxMac, string txMac, string streamType, int streamIndex)
        {
            if (FailJoin) return Task.FromResult(false);
            lock (AllCalls) AllCalls.Add($"join:{rxMac}:{txMac}");
            if (streamType == "HDMI")
            {
                LastVideoRx = rxMac;
                LastVideoTx = txMac;
            }
            return Task.FromResult(true);
        }

        /// <summary>Every leave, as "rxMac:streamIndex".</summary>
        public System.Collections.Generic.List<string> Leaves { get; } = new();

        public Task<bool> LeaveStreamAsync(string rxMac, string streamType, int streamIndex)
        {
            LastUnroutedRx = rxMac;
            lock (Leaves) Leaves.Add($"{rxMac}:{streamIndex}");
            return Task.FromResult(true);
        }

        public Task<bool> StartAvasPreviewAsync(string mac, string multicastIp)
        {
            return Task.FromResult(true);
        }

        public Task<bool> StopAvasPreviewAsync(string mac)
        {
            return Task.FromResult(true);
        }

        public Task<bool> StartStream1ScalerAsync(string txMac, string multicastIp, int port = 5004)
        {
            return Task.FromResult(true);
        }

        public Task<bool> StopStream1ScalerAsync(string txMac)
        {
            return Task.FromResult(true);
        }

        /// <summary>Ordered log of multiview calls, e.g. "scaler:74fe..:960x540", "layout:avas_..", "multiview:rx:layout:tx1@1,tx2@2".</summary>
        public System.Collections.Generic.List<string> MultiViewCalls { get; } = new();

        /// <summary>When set, the multiview call whose log entry starts with this prefix reports failure.</summary>
        public string? FailMultiViewPrefix { get; set; }

        public System.Collections.Generic.IReadOnlyList<AvasRoutingTool.Sdvoe.MultiView.MultiViewSubscription>? LastSubscriptions { get; private set; }

        public int LastFps { get; private set; }

        /// <summary>
        /// Awaited by every multiview call after it is logged: lets a test hold one call in flight (so concurrent UI
        /// actions overlap deterministically) and release it later.
        /// </summary>
        public System.Func<string, Task>? DuringCall { get; set; }

        /// <summary>Every multiview call and every accepted join, in the order they were made (joins are not in <see cref="MultiViewCalls"/>).</summary>
        public System.Collections.Generic.List<string> AllCalls { get; } = new();

        private async Task<bool> Record(string entry)
        {
            lock (MultiViewCalls) MultiViewCalls.Add(entry);
            lock (AllCalls) AllCalls.Add(entry);
            if (DuringCall != null) await DuringCall(entry);
            return FailMultiViewPrefix == null || !entry.StartsWith(FailMultiViewPrefix);
        }

        /// <summary>The windows and surfaces of the last layout created.</summary>
        public AvasRoutingTool.Sdvoe.MultiView.SurfacePlan? LastSurfacePlan { get; private set; }

        public Task<bool> CreateMultiViewLayoutAsync(AvasRoutingTool.Sdvoe.MultiView.SurfacePlan surfacePlan,
            AvasRoutingTool.Sdvoe.MultiView.MultiViewLayoutPlan plan)
        {
            LastSurfacePlan = surfacePlan;
            return Record($"layout:{surfacePlan.LayoutName}");
        }

        public Task<bool> PrepareSourceScalerAsync(string txMac, int width, int height) =>
            Record($"scaler:{txMac}:{width}x{height}");

        /// <summary>Output size of the last <c>set:multiview</c>.</summary>
        public (int Width, int Height) LastMultiViewSize { get; private set; }

        public Task<bool> SetMultiViewAsync(string rxMac, string layoutName, int width, int height, int fps,
            System.Collections.Generic.IReadOnlyList<AvasRoutingTool.Sdvoe.MultiView.MultiViewSubscription> subscriptions)
        {
            LastSubscriptions = subscriptions;
            LastFps = fps;
            LastMultiViewSize = (width, height);
            return Record($"multiview:{rxMac}:{layoutName}:{string.Join(",", System.Linq.Enumerable.Select(subscriptions, s => $"{s.SourceMac}@{s.SubscriptionIndex}"))}");
        }

        /// <summary>Like the hardware: an accepted mode change shows in what the receiver reports next.</summary>
        public bool ThrowOnFastSwitch { get; set; }

        public bool ThrowOnReceiverRead { get; set; }

        public async Task<bool> SetFastSwitchedAsync(string rxMac, int width, int height, int fps)
        {
            if (ThrowOnFastSwitch) throw new System.InvalidOperationException("set:video blew up");
            bool ok = await Record($"fastswitch:{rxMac}:{width}x{height}@{fps}");
            if (ok) ReceiverStates[rxMac] = new AvasRoutingTool.Sdvoe.RxState(rxMac, rxMac, "FAST_SWITCHED", width, height, fps, System.Array.Empty<AvasRoutingTool.Sdvoe.RxSubscription>(), null);
            return ok;
        }

        /// <summary>State returned by <see cref="ReadReceiverStateAsync"/> per receiver MAC (missing = unreadable).</summary>
        public System.Collections.Generic.Dictionary<string, AvasRoutingTool.Sdvoe.RxState?> ReceiverStates { get; } = new();

        /// <summary>Optional artificial delay per receiver MAC, to prove reads run in parallel.</summary>
        public System.Collections.Generic.Dictionary<string, System.TimeSpan> ReceiverReadDelay { get; } = new();

        public System.Collections.Generic.List<string> ReceiverReads { get; } = new();

        public async Task<AvasRoutingTool.Sdvoe.RxState?> ReadReceiverStateAsync(string rxMac)
        {
            if (ThrowOnReceiverRead) throw new System.InvalidOperationException("get:state blew up");
            lock (ReceiverReads) ReceiverReads.Add(rxMac);
            if (ReceiverReadDelay.TryGetValue(rxMac, out var delay)) await Task.Delay(delay);
            return ReceiverStates.TryGetValue(rxMac, out var s) ? s : null;
        }

        /// <summary>Incoming video per transmitter MAC for <see cref="ReadSourceVideoAsync"/>; a missing one reads as unknown.</summary>
        public System.Collections.Generic.Dictionary<string, AvasRoutingTool.Sdvoe.MultiView.SourceVideo> SourceVideos { get; } = new();

        public Task<AvasRoutingTool.Sdvoe.MultiView.SourceVideo?> ReadSourceVideoAsync(string txMac) =>
            Task.FromResult(SourceVideos.TryGetValue(txMac, out var v) ? v : null);

        /// <summary>MACs whose stream-1 divider was already on before the tool touched anything.</summary>
        public System.Collections.Generic.HashSet<string> AlreadyHalved { get; } = new();

        public Task<bool?> ReadStream1FrameDividerAsync(string txMac) =>
            Task.FromResult<bool?>(AlreadyHalved.Contains(txMac));

        public Task<bool> SetStream1FrameDividerAsync(string txMac, bool halve) =>
            Record($"divider:{txMac}:{(halve ? "half" : "full")}");

        /// <summary>EDID hex returned for any receiver; null means "no display / read failed".</summary>
        public string? DisplayEdid { get; set; }

        /// <summary>EDID hex per receiver MAC; a receiver missing here gets <see cref="DisplayEdid"/>.</summary>
        public System.Collections.Generic.Dictionary<string, string?> DisplayEdids { get; } = new();

        /// <summary>How many times the display EDID was read.</summary>
        public int EdidReads => _edidReads;
        private int _edidReads;

        /// <summary>Every EDID read, by receiver MAC.</summary>
        public System.Collections.Generic.List<string> EdidReadsBy { get; } = new();

        /// <summary>Awaited by every EDID read (after it is counted): lets a test hold overlapping layout selections in flight.</summary>
        public System.Func<Task>? DuringEdidRead { get; set; }

        public async Task<string?> ReadDisplayEdidAsync(string rxMac)
        {
            System.Threading.Interlocked.Increment(ref _edidReads);
            lock (EdidReadsBy) EdidReadsBy.Add(rxMac);
            if (DuringEdidRead != null) await DuringEdidRead();
            return DisplayEdids.TryGetValue(rxMac, out var hex) ? hex : DisplayEdid;
        }

        public Task<System.Collections.Generic.List<AvasDevice>> DiscoverDevicesAsync()
        {
            return Task.FromResult(new System.Collections.Generic.List<AvasDevice>());
        }

        public void Dispose()
        {
        }
    }

    public class RoutingOrchestratorTests
    {
        [Fact]
        public async Task RouteSingleViewVideoAsync_RoutesVideoAndUpdatesDeviceName()
        {
            var mock = new MockSdvoeClient();
            var orchestrator = new RoutingOrchestrator(mock);

            var tx = new AvasDevice { DeviceId = "00:11:22:33:44:AA", DeviceName = "Bron 1", IsTransmitter = true };
            var rx = new AvasDevice { DeviceId = "00:11:22:33:44:BB", DeviceName = "Samsung 1", IsReceiver = true };

            bool success = await orchestrator.RouteSingleViewVideoAsync(tx, rx);

            Assert.True(success);
            Assert.Equal("00:11:22:33:44:BB", mock.LastVideoRx);
            Assert.Equal("00:11:22:33:44:AA", mock.LastVideoTx);
            Assert.Equal("00:11:22:33:44:AA", rx.ActiveSourceId);
            Assert.Equal("Bron 1", rx.ActiveSourceName);
        }

        [Fact]
        public async Task UnrouteDisplayAsync_ClearsSubscriptions()
        {
            var mock = new MockSdvoeClient();
            var orchestrator = new RoutingOrchestrator(mock);

            var rx = new AvasDevice { DeviceId = "00:11:22:33:44:BB", DeviceName = "Samsung 1", ActiveSourceId = "00:11:22:33:44:AA", ActiveSourceName = "Bron 1" };
            bool success = await orchestrator.UnrouteDisplayAsync(rx);

            Assert.True(success);
            Assert.Equal("00:11:22:33:44:BB", mock.LastUnroutedRx);
            Assert.Empty(rx.ActiveSourceId);
            Assert.Empty(rx.ActiveSourceName);
        }

        // ---- Single View runs in Fast Switch at the display's EDID format ----

        private const string RxMac = "00:11:22:33:44:BB";
        private const string TxMac = "00:11:22:33:44:AA";

        private static (AvasDevice Tx, AvasDevice Rx) Pair() => (
            new AvasDevice { DeviceId = TxMac, DeviceName = "Bron 1", IsTransmitter = true },
            new AvasDevice { DeviceId = RxMac, DeviceName = "Samsung 1", IsReceiver = true });

        private static RxState StateOf(string mode, int w, int h, int fps) =>
            new(RxMac, "Samsung 1", mode, w, h, fps, System.Array.Empty<RxSubscription>(), null);

        [Fact]
        public async Task Route_SetsFastSwitchAtTheEdidFormat_BeforeTheJoin()
        {
            var mock = new MockSdvoeClient { DisplayEdid = TestEdids.Build((3840, 2160, 60, false)) };
            var (tx, rx) = Pair();

            var result = await new RoutingOrchestrator(mock).RouteSingleViewAsync(tx, rx);

            Assert.True(result.Ok);
            Assert.Equal(new[] { $"fastswitch:{RxMac}:3840x2160@60", $"join:{RxMac}:{TxMac}" }, mock.AllCalls);
            Assert.True(result.Format!.FromDisplay);
            Assert.Equal((3840, 2160, 60), (result.Format.Width, result.Format.Height, result.Format.Fps));
        }

        [Fact]
        public async Task Route_WithoutAnEdid_FallsBackTo1080p60_AndStillJoins()
        {
            var mock = new MockSdvoeClient { DisplayEdid = null };
            var (tx, rx) = Pair();

            var result = await new RoutingOrchestrator(mock).RouteSingleViewAsync(tx, rx);

            Assert.True(result.Ok);
            Assert.Equal(new[] { $"fastswitch:{RxMac}:1920x1080@60", $"join:{RxMac}:{TxMac}" }, mock.AllCalls);
            Assert.False(result.Format!.FromDisplay);
            Assert.Contains("display EDID unavailable", result.Format.Description);
        }

        [Fact]
        public async Task Route_WhenSetVideoIsRefused_SendsNoJoin_AndKeepsTheActiveSource()
        {
            var mock = new MockSdvoeClient { DisplayEdid = TestEdids.Build((1920, 1080, 60, false)), FailMultiViewPrefix = "fastswitch:" };
            var (tx, rx) = Pair();
            rx.ActiveSourceId = "00:11:22:33:44:CC";
            rx.ActiveSourceName = "Earlier";

            var result = await new RoutingOrchestrator(mock).RouteSingleViewAsync(tx, rx);

            Assert.False(result.Ok);
            Assert.Contains("Fast Switch 1920x1080@60", result.Error);
            Assert.DoesNotContain(mock.AllCalls, c => c.StartsWith("join:"));
            Assert.Equal("00:11:22:33:44:CC", rx.ActiveSourceId);
            Assert.Equal("Earlier", rx.ActiveSourceName);
            Assert.False(await new RoutingOrchestrator(mock).RouteSingleViewVideoAsync(tx, rx));
        }

        [Fact]
        public async Task Route_WhenSetVideoThrows_RefusesTheRoute_SendsNoJoin_AndKeepsTheActiveSource()
        {
            var mock = new MockSdvoeClient { DisplayEdid = TestEdids.Build((1920, 1080, 60, false)), ThrowOnFastSwitch = true };
            var (tx, rx) = Pair();
            rx.ActiveSourceId = "00:11:22:33:44:CC";

            var result = await new RoutingOrchestrator(mock).RouteSingleViewAsync(tx, rx);

            Assert.False(result.Ok);
            Assert.Contains("Fast Switch 1920x1080@60", result.Error);
            Assert.DoesNotContain(mock.AllCalls, c => c.StartsWith("join:"));
            Assert.Equal("00:11:22:33:44:CC", rx.ActiveSourceId);
        }

        [Fact]
        public async Task Route_WhenTheStateReadThrows_TreatsItAsUnreadable_AndStillSetsTheMode()
        {
            var mock = new MockSdvoeClient { DisplayEdid = TestEdids.Build((1920, 1080, 60, false)), ThrowOnReceiverRead = true };
            var (tx, rx) = Pair();

            var result = await new RoutingOrchestrator(mock).RouteSingleViewAsync(tx, rx);

            Assert.True(result.Ok);
            Assert.Equal(new[] { $"fastswitch:{RxMac}:1920x1080@60", $"join:{RxMac}:{TxMac}" }, mock.AllCalls);
        }

        [Fact]
        public async Task EnsureSingleViewMode_WhenSetVideoThrows_ReturnsNotOk_ForMultiViewExits()
        {
            var mock = new MockSdvoeClient { ThrowOnFastSwitch = true };
            var (_, rx) = Pair();

            var mode = await new RoutingOrchestrator(mock).EnsureSingleViewModeAsync(rx);

            Assert.False(mode.Ok);
        }

        [Fact]
        public async Task Route_SkipsSetVideo_WhenTheRxIsAlreadyFastSwitchedAtTheFormat()
        {
            var mock = new MockSdvoeClient { DisplayEdid = TestEdids.Build((1920, 1080, 60, false)) };
            mock.ReceiverStates[RxMac] = StateOf("FAST_SWITCHED", 1920, 1080, 60);
            var (tx, rx) = Pair();

            var result = await new RoutingOrchestrator(mock).RouteSingleViewAsync(tx, rx);

            Assert.True(result.Ok);
            Assert.Equal(new[] { $"join:{RxMac}:{TxMac}" }, mock.AllCalls);
        }

        [Theory]
        [InlineData("FAST_SWITCHED_CROP", 1920, 1080)]
        [InlineData("FAST_SWITCHED_STRETCH", 1920, 1080)]
        [InlineData("GENLOCKED", 1920, 1080)]
        [InlineData("FAST_SWITCHED", 1280, 720)]
        [InlineData(null, 0, 0)] // state unreadable: it cannot be proven correct
        public async Task Route_SendsSetVideo_WhenTheRxIsInAnotherModeOrSize_OrUnreadable(string? mode, int w, int h)
        {
            var mock = new MockSdvoeClient { DisplayEdid = TestEdids.Build((1920, 1080, 60, false)) };
            if (mode != null) mock.ReceiverStates[RxMac] = StateOf(mode, w, h, 60);
            var (tx, rx) = Pair();

            Assert.True((await new RoutingOrchestrator(mock).RouteSingleViewAsync(tx, rx)).Ok);

            Assert.Equal(new[] { $"fastswitch:{RxMac}:1920x1080@60", $"join:{RxMac}:{TxMac}" }, mock.AllCalls);
        }

        [Fact]
        public async Task Route_SecondTxToTheSameRx_ChangesOnlyTheJoin()
        {
            var mock = new MockSdvoeClient { DisplayEdid = TestEdids.Build((1920, 1080, 60, false)) };
            var (tx, rx) = Pair();
            var orchestrator = new RoutingOrchestrator(mock);
            await orchestrator.RouteSingleViewAsync(tx, rx);
            mock.ReceiverStates[RxMac] = StateOf("FAST_SWITCHED", 1920, 1080, 60); // what the hardware now reports
            mock.AllCalls.Clear();

            var tx2 = new AvasDevice { DeviceId = "00:11:22:33:44:DD", DeviceName = "Bron 2", IsTransmitter = true };
            Assert.True((await orchestrator.RouteSingleViewAsync(tx2, rx)).Ok);

            Assert.Equal(new[] { $"join:{RxMac}:00:11:22:33:44:DD" }, mock.AllCalls);
        }
    }
}