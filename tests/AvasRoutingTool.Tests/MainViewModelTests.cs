using System.Linq;
using System.Threading.Tasks;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Rtp;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class MockRtpReceiver : IRtpReceiver
    {
        public bool IsListening { get; private set; }

        public void StartListening(int[]? ports = null, string? localIp = null) => IsListening = true;
        public void StopListening() => IsListening = false;
        public void JoinGroup(string multicastIp) { }
        public void DropGroup(string multicastIp) { }
        public void RegisterChannel(string multicastIp, System.Action<byte[], int, int> packetHandler) { }
        public void UnregisterChannel(string multicastIp) { }
        public void Dispose() => StopListening();
    }

    public class MainViewModelTests
    {
        [Fact]
        public void Layouts_SingleViewIsActive_AndMultiViewsArePhase2()
        {
            var configService = new ConfigService();
            var sdvoeClient = new MockSdvoeClient();
            var rtpReceiver = new MockRtpReceiver();
            var ipManager = new MulticastIpManager();
            var orchestrator = new RoutingOrchestrator(sdvoeClient);

            var vm = new MainViewModel(configService, sdvoeClient, rtpReceiver, ipManager, orchestrator);

            var single = vm.Layouts.FirstOrDefault(l => l.Id == "single");
            Assert.NotNull(single);
            Assert.True(single.IsPhase1Supported);
            Assert.True(single.IsSelected);
            Assert.Equal("ACTIVE", single.BadgeText);

            var matrix = vm.Layouts.FirstOrDefault(l => l.Id == "matrix2x2");
            Assert.NotNull(matrix);
            Assert.False(matrix.IsPhase1Supported);
            Assert.Equal("PHASE 2", matrix.BadgeText);
        }

        [Fact]
        public async Task RouteVideoAsync_InvokesOrchestratorAndUpdatesDisplay()
        {
            var configService = new ConfigService();
            var sdvoeClient = new MockSdvoeClient();
            var rtpReceiver = new MockRtpReceiver();
            var ipManager = new MulticastIpManager();
            var orchestrator = new RoutingOrchestrator(sdvoeClient);

            var vm = new MainViewModel(configService, sdvoeClient, rtpReceiver, ipManager, orchestrator);

            var tx = new AvasDevice { DeviceId = "00:11:22:33:44:01", DeviceName = "Source 1", IsTransmitter = true, IsActive = true };
            var rx = new AvasDevice { DeviceId = "00:11:22:33:44:02", DeviceName = "Display 1", IsReceiver = true, IsActive = true };

            var rxVm = new DisplayCardViewModel(rx, vm.UnrouteVideoAsync);
            vm.Displays.Add(rxVm);

            bool success = await vm.RouteVideoAsync(tx, rx);

            Assert.True(success);
            Assert.Equal("00:11:22:33:44:01", rxVm.ActiveSourceId);
            Assert.Equal("Source 1", rxVm.ActiveSourceName);
            Assert.True(rxVm.IsRouted);

            // Now test unrouting
            bool unrouteSuccess = await vm.UnrouteVideoAsync(rx);
            Assert.True(unrouteSuccess);
            Assert.Empty(rxVm.ActiveSourceId);
            Assert.Empty(rxVm.ActiveSourceName);
            Assert.False(rxVm.IsRouted);
        }
    }
}
