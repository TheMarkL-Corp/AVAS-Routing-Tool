using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>The connection state behind the app bar dot (M8): Connecting until the connect call returns, then Connected or Offline.</summary>
    public class MainViewModelConnectionTests
    {
        private sealed class StubConfig : IConfigService
        {
            public AppConfig Load() => new AppConfig { ShowLogs = false };
            public void Save(AppConfig config) { }
        }

        private static MainViewModel Build(MockSdvoeClient client) =>
            new MainViewModel(new StubConfig(), client, new MockRtpReceiver(), new MulticastIpManager(), new RoutingOrchestrator(client));

        [Fact]
        public void ConnectionHealth_StartsConnecting()
        {
            var vm = Build(new MockSdvoeClient());

            Assert.Equal(ConnectionHealth.Connecting, vm.ConnectionHealth);
        }

        [Fact]
        public async Task ConnectionHealth_IsConnecting_WhileTheConnectCallIsPending()
        {
            var gate = new TaskCompletionSource<bool>();
            var client = new MockSdvoeClient { ConnectGate = gate.Task };
            var vm = Build(client);

            var init = vm.InitializeAsync();

            Assert.Equal(ConnectionHealth.Connecting, vm.ConnectionHealth);
            Assert.StartsWith("Connecting to ", vm.ConnectionStatus);

            gate.SetResult(true);
            await init;
            Assert.Equal(ConnectionHealth.Connected, vm.ConnectionHealth);
        }

        [Fact]
        public async Task ConnectionHealth_BecomesConnected_WhenTheClientConnects()
        {
            var vm = Build(new MockSdvoeClient { ConnectResult = true });

            await vm.InitializeAsync();

            Assert.Equal(ConnectionHealth.Connected, vm.ConnectionHealth);
            Assert.True(vm.IsConnected);
            Assert.StartsWith("Connected: ", vm.ConnectionStatus);
        }

        [Fact]
        public async Task ConnectionHealth_BecomesOffline_WhenTheConnectFails()
        {
            var vm = Build(new MockSdvoeClient { ConnectResult = false });

            await vm.InitializeAsync();

            Assert.Equal(ConnectionHealth.Offline, vm.ConnectionHealth);
            Assert.False(vm.IsConnected);
            Assert.StartsWith("Offline: ", vm.ConnectionStatus);
        }

        [Fact]
        public async Task ConnectionHealth_RaisesPropertyChanged()
        {
            var vm = Build(new MockSdvoeClient { ConnectResult = true });
            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            await vm.InitializeAsync();

            Assert.Contains(nameof(MainViewModel.ConnectionHealth), changed);
        }
    }
}
