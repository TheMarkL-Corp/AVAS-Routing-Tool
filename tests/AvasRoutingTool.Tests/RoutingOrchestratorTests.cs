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

        public Task<bool> LeaveStreamAsync(string rxMac, string streamType, int streamIndex)
        {
            LastUnroutedRx = rxMac;
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
