using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvasRoutingTool.Rtsp;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Tests.Fakes;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class RtspLifecycleTests
    {
        private const string FakeUrl = "rtsp://user:pass-fake@example.test:554/live/1";

        private static (MainViewModel Vm, List<FakeRtspPlayer> Players) Rig()
        {
            var client = new MockSdvoeClient();
            var vm = new MainViewModel(new Configuration.ConfigService(), client, new MockRtpReceiver(),
                new MulticastIpManager(), new RoutingOrchestrator(client));
            var players = new List<FakeRtspPlayer>();
            vm.RtspPlayerFactory = () => { var p = new FakeRtspPlayer(); players.Add(p); return p; };
            // Never completes: a Lost stream stays Lost until cancelled.
            vm.RtspDelay = (_, ct) => Task.Delay(Timeout.Infinite, ct);
            return (vm, players);
        }

        private static (MainViewModel Vm, List<FakeRtspPlayer> Players) Started()
        {
            var r = Rig();
            Assert.True(r.Vm.TrySetRtspUrl(FakeUrl, out _));
            return r;
        }

        [Fact]
        public void LeavingVisualRoutingTab_StopsPlayer()
        {
            var (vm, players) = Started();
            players[0].Raise(RtspPlayerEvent.Playing);
            var before = players[0].StopCount;

            vm.OnVisualRoutingVisibilityChanged(false);

            Assert.Equal(RtspTileState.Stopped, vm.ViewOnlySource!.State);
            Assert.Equal("Stopped", vm.ViewOnlySource.StatusText);
            Assert.True(players[0].StopCount > before);
        }

        [Theory]
        [InlineData("Connecting", true)]
        [InlineData("Live", true)]
        [InlineData("Lost", true)]
        [InlineData("UserStopped", false)]
        [InlineData("Error", false)]
        public void ReturningToTab_RestartsOnlyStreamsThatWereRunning(string was, bool restarted)
        {
            var (vm, players) = Started();
            switch (was)
            {
                case "Live": players[0].Raise(RtspPlayerEvent.Playing); break;
                case "Lost": players[0].Raise(RtspPlayerEvent.Dropped); break;
                case "UserStopped": vm.ViewOnlySource!.StopCommand.Execute(null); break;
                case "Error": players[0].Raise(RtspPlayerEvent.Failed(RtspFailure.AuthFailed)); break;
            }
            var startsBefore = players.Sum(p => p.Started.Count);

            vm.OnVisualRoutingVisibilityChanged(false);
            Assert.Equal(RtspTileState.Stopped, vm.ViewOnlySource!.State);
            vm.OnVisualRoutingVisibilityChanged(true);

            Assert.Equal(restarted ? RtspTileState.Connecting : (was == "Error" ? RtspTileState.Stopped : RtspTileState.Stopped), vm.ViewOnlySource.State);
            Assert.Equal(startsBefore + (restarted ? 1 : 0), players.Sum(p => p.Started.Count));
        }

        [Fact]
        public void WindowClose_DisposesPlayer_AndLateEventsAreIgnored()
        {
            var (vm, players) = Started();
            players[0].Raise(RtspPlayerEvent.Playing);
            var source = vm.ViewOnlySource!;
            var changes = new List<string?>();
            source.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

            vm.Dispose();

            Assert.True(players[0].Disposed);
            changes.Clear();
            var ex = Record.Exception(() =>
            {
                players[0].Raise(RtspPlayerEvent.Playing);
                players[0].Raise(RtspPlayerEvent.Dropped);
                players[0].SwapBitmap(new System.Windows.Media.Imaging.WriteableBitmap(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null));
            });
            Assert.Null(ex);
            Assert.Equal(RtspTileState.Stopped, source.State);
            Assert.Empty(changes);
            Assert.Null(vm.ViewOnlySource);
        }

        [Fact]
        public void TabSwitchWhileConnecting_CancelsConnect_NoLateEventRevivesTile()
        {
            var (vm, players) = Started();
            Assert.Equal(RtspTileState.Connecting, vm.ViewOnlySource!.State);

            vm.OnVisualRoutingVisibilityChanged(false);
            players[0].Raise(RtspPlayerEvent.Playing);

            Assert.Equal(RtspTileState.Stopped, vm.ViewOnlySource.State);
            Assert.Equal("Stopped", vm.ViewOnlySource.StatusText);
        }

        [Fact]
        public void ToggleShowLogsAndShowAdvanced_DoNotRestartOrStop()
        {
            var (vm, players) = Started();
            players[0].Raise(RtspPlayerEvent.Playing);
            var stops = players[0].StopCount;
            var starts = players[0].Started.Count;

            vm.ShowLogs = !vm.ShowLogs;
            vm.ShowAdvanced = !vm.ShowAdvanced;
            vm.ShowLogs = !vm.ShowLogs;
            vm.ShowAdvanced = !vm.ShowAdvanced;

            Assert.Equal(RtspTileState.Live, vm.ViewOnlySource!.State);
            Assert.Equal(stops, players[0].StopCount);
            Assert.Equal(starts, players[0].Started.Count);
        }

        [Fact]
        public void ReturnToTab_WithNoStream_DoesNothing()
        {
            var (vm, players) = Rig();
            var ex = Record.Exception(() =>
            {
                vm.OnVisualRoutingVisibilityChanged(false);
                vm.OnVisualRoutingVisibilityChanged(true);
            });
            Assert.Null(ex);
            Assert.Null(vm.ViewOnlySource);
            Assert.Empty(players);
        }

        [Fact]
        public void RepeatedLeaveEnter_DoesNotLeakPlayers()
        {
            var (vm, players) = Started();
            players[0].Raise(RtspPlayerEvent.Playing);

            for (var i = 0; i < 5; i++)
            {
                vm.OnVisualRoutingVisibilityChanged(false);
                vm.OnVisualRoutingVisibilityChanged(true);
                players.Last().Raise(RtspPlayerEvent.Playing);
            }
            // A repeated "left" must not forget that the stream was running.
            vm.OnVisualRoutingVisibilityChanged(false);
            vm.OnVisualRoutingVisibilityChanged(false);
            vm.OnVisualRoutingVisibilityChanged(true);
            Assert.Equal(RtspTileState.Connecting, vm.ViewOnlySource!.State);

            Assert.Equal(1, players.Count(p => !p.Disposed));

            vm.Dispose();
            Assert.All(players, p => Assert.True(p.Disposed));
        }
    }
}
