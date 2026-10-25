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

        public Task<bool> ConnectAsync(string host, int telnetPort = 6970, int restPort = 8080)
        {
            return Task.FromResult(true);
        }

        public Task DisconnectAsync()
        {
            return Task.CompletedTask;
        }

        public Task<bool> JoinStreamAsync(string rxMac, string txMac, string streamType, int streamIndex)
        {
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

        private async Task<bool> Record(string entry)
        {
            lock (MultiViewCalls) MultiViewCalls.Add(entry);
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

        public Task<bool> SetMultiViewAsync(string rxMac, string layoutName, int width, int height, int fps,
            System.Collections.Generic.IReadOnlyList<AvasRoutingTool.Sdvoe.MultiView.MultiViewSubscription> subscriptions)
        {
            LastSubscriptions = subscriptions;
            LastFps = fps;
            return Record($"multiview:{rxMac}:{layoutName}:{string.Join(",", System.Linq.Enumerable.Select(subscriptions, s => $"{s.SourceMac}@{s.SubscriptionIndex}"))}");
        }

        public Task<bool> SetGenlockedAsync(string rxMac) => Record($"genlock:{rxMac}");

        /// <summary>State returned by <see cref="ReadReceiverStateAsync"/> per receiver MAC (missing = unreadable).</summary>
        public System.Collections.Generic.Dictionary<string, AvasRoutingTool.Sdvoe.RxState?> ReceiverStates { get; } = new();

        /// <summary>Optional artificial delay per receiver MAC, to prove reads run in parallel.</summary>
        public System.Collections.Generic.Dictionary<string, System.TimeSpan> ReceiverReadDelay { get; } = new();

        public System.Collections.Generic.List<string> ReceiverReads { get; } = new();

        public async Task<AvasRoutingTool.Sdvoe.RxState?> ReadReceiverStateAsync(string rxMac)
        {
            lock (ReceiverReads) ReceiverReads.Add(rxMac);
            if (ReceiverReadDelay.TryGetValue(rxMac, out var delay)) await Task.Delay(delay);
            return ReceiverStates.TryGetValue(rxMac, out var s) ? s : null;
        }

        /// <summary>MACs whose stream-1 divider was already on before the tool touched anything.</summary>
        public System.Collections.Generic.HashSet<string> AlreadyHalved { get; } = new();

        public Task<bool?> ReadStream1FrameDividerAsync(string txMac) =>
            Task.FromResult<bool?>(AlreadyHalved.Contains(txMac));

        public Task<bool> SetStream1FrameDividerAsync(string txMac, bool halve) =>
            Record($"divider:{txMac}:{(halve ? "half" : "full")}");

        /// <summary>EDID hex returned for any receiver; null means "no display / read failed".</summary>
        public string? DisplayEdid { get; set; }

        /// <summary>How many times the display EDID was read.</summary>
        public int EdidReads { get; private set; }

        public Task<string?> ReadDisplayEdidAsync(string rxMac)
        {
            EdidReads++;
            return Task.FromResult(DisplayEdid);
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
    }
}
