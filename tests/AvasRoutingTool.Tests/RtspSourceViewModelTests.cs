using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvasRoutingTool.Rtsp;
using AvasRoutingTool.Tests.Fakes;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class RtspSourceViewModelTests
    {
        private const string FakeUrl = "rtsp://user:pass-fake@example.test:554/live/1?token=abc";

        private sealed class Rig
        {
            public FakeRtspPlayer Player = new();
            public List<TimeSpan> Delays = new();
            public List<CancellationToken> Tokens = new();
            public TaskCompletionSource<bool>? Gate;
            public RtspSourceViewModel Vm;
            public int Created;

            public Rig(bool gated = false)
            {
                RtspUrl.TryParse(FakeUrl, out var url, out _);
                if (gated) Gate = new TaskCompletionSource<bool>();
                Vm = new RtspSourceViewModel(url, () => { Created++; return Player; }, async (d, ct) =>
                {
                    Delays.Add(d); Tokens.Add(ct);
                    if (Gate != null) await Gate.Task.WaitAsync(ct);
                });
            }
        }

        [Fact]
        public void Start_GoesConnecting_ThenLiveOnPlaying()
        {
            var r = new Rig();
            Assert.Equal(RtspTileState.Stopped, r.Vm.State);
            r.Vm.StartCommand.Execute(null);
            Assert.Equal(RtspTileState.Connecting, r.Vm.State);
            Assert.Equal("Connecting...", r.Vm.StatusText);
            Assert.Equal(new[] { FakeUrl }, r.Player.Started);
            r.Player.Raise(RtspPlayerEvent.Playing);
            Assert.Equal(RtspTileState.Live, r.Vm.State);
            Assert.Equal("Live", r.Vm.StatusText);
        }

        [Fact]
        public void Dropped_GoesLost_AndRetriesWithPolicyDelays()
        {
            var r = new Rig();
            r.Vm.StartCommand.Execute(null);
            r.Player.Raise(RtspPlayerEvent.Playing);
            for (var i = 0; i < 5; i++) r.Player.Raise(RtspPlayerEvent.Dropped);
            Assert.Equal(new[] { 1, 2, 5, 10, 10 }, r.Delays.Select(d => (int)d.TotalSeconds).ToArray());
            Assert.Equal(6, r.Player.Started.Count);
            Assert.Equal(RtspTileState.Connecting, r.Vm.State);
        }

        [Fact]
        public void Dropped_ShowsLostWithSecondsWhileWaiting()
        {
            var r = new Rig(gated: true);
            r.Vm.StartCommand.Execute(null);
            r.Player.Raise(RtspPlayerEvent.Playing);
            r.Player.Raise(RtspPlayerEvent.Dropped);
            Assert.Equal(RtspTileState.Lost, r.Vm.State);
            Assert.Equal("Connection lost - retrying in 1 s", r.Vm.StatusText);
        }

        [Fact]
        public void RetrySucceeds_ReturnsToLive_ResetsAttemptCounter()
        {
            var r = new Rig();
            r.Vm.StartCommand.Execute(null);
            r.Player.Raise(RtspPlayerEvent.Dropped);
            r.Player.Raise(RtspPlayerEvent.Dropped);
            r.Player.Raise(RtspPlayerEvent.Playing);
            Assert.Equal(RtspTileState.Live, r.Vm.State);
            r.Player.Raise(RtspPlayerEvent.Dropped);
            Assert.Equal(new[] { 1, 2, 1 }, r.Delays.Select(d => (int)d.TotalSeconds).ToArray());
        }

        [Fact]
        public void AuthFailed_GoesError_NoRetry()
        {
            var r = new Rig();
            r.Vm.StartCommand.Execute(null);
            r.Player.Raise(RtspPlayerEvent.Failed(RtspFailure.AuthFailed));
            Assert.Equal(RtspTileState.Error, r.Vm.State);
            Assert.StartsWith("Cannot open stream: ", r.Vm.StatusText);
            Assert.Empty(r.Delays);
            Assert.Single(r.Player.Started);
        }

        [Fact]
        public void NotFound_GoesError_NoRetry()
        {
            var r = new Rig();
            r.Vm.StartCommand.Execute(null);
            r.Player.Raise(RtspPlayerEvent.Failed(RtspFailure.NotFound));
            Assert.Equal(RtspTileState.Error, r.Vm.State);
            Assert.StartsWith("Cannot open stream: ", r.Vm.StatusText);
            Assert.Empty(r.Delays);
            Assert.Single(r.Player.Started);
        }

        [Fact]
        public void Unreachable_AtStart_RetriesAutomatically()
        {
            var r = new Rig();
            r.Vm.StartCommand.Execute(null);
            r.Player.Raise(RtspPlayerEvent.Failed(RtspFailure.Unreachable));
            Assert.Single(r.Delays);
            Assert.Equal(2, r.Player.Started.Count);
            Assert.Equal(RtspTileState.Connecting, r.Vm.State);
        }

        [Fact]
        public void Stop_CancelsPendingRetry_AndStopsPlayer()
        {
            var r = new Rig(gated: true);
            r.Vm.StartCommand.Execute(null);
            r.Player.Raise(RtspPlayerEvent.Dropped);
            r.Vm.StopCommand.Execute(null);
            Assert.Equal(RtspTileState.Stopped, r.Vm.State);
            Assert.Equal("Stopped", r.Vm.StatusText);
            Assert.True(r.Tokens[0].IsCancellationRequested);
            Assert.True(r.Player.StopCount >= 1);
            r.Gate!.TrySetResult(true);
            Assert.Single(r.Player.Started);
            Assert.Equal(RtspTileState.Stopped, r.Vm.State);
        }

        [Fact]
        public void LateEventAfterStop_DoesNotReviveTile()
        {
            var r = new Rig();
            r.Vm.StartCommand.Execute(null);
            r.Vm.StopCommand.Execute(null);
            r.Player.Raise(RtspPlayerEvent.Playing);
            Assert.Equal(RtspTileState.Stopped, r.Vm.State);
            r.Player.Raise(RtspPlayerEvent.Dropped);
            Assert.Equal(RtspTileState.Stopped, r.Vm.State);
            Assert.Empty(r.Delays);
        }

        [Fact]
        public void Dispose_StopsPlayer_NoFurtherEvents()
        {
            var r = new Rig();
            r.Vm.StartCommand.Execute(null);
            r.Vm.Dispose();
            Assert.True(r.Player.StopCount >= 1);
            Assert.True(r.Player.Disposed);
            r.Player.Raise(RtspPlayerEvent.Playing);
            Assert.Equal(RtspTileState.Stopped, r.Vm.State);
        }

        [Fact]
        public void RetryCommand_FromError_RestartsAndGoesConnecting()
        {
            var r = new Rig();
            r.Vm.StartCommand.Execute(null);
            r.Player.Raise(RtspPlayerEvent.Failed(RtspFailure.AuthFailed));
            Assert.True(r.Vm.RetryCommand.CanExecute(null));
            r.Vm.RetryCommand.Execute(null);
            Assert.Equal(RtspTileState.Connecting, r.Vm.State);
            Assert.Equal(2, r.Player.Started.Count);
        }

        [Fact]
        public void StatusText_NeverContainsPasswordOrHost()
        {
            var r = new Rig(gated: true);
            var seen = new List<(string Status, string Masked)>();
            void Snap() => seen.Add((r.Vm.StatusText, r.Vm.MaskedAddress));
            Snap();
            r.Vm.StartCommand.Execute(null); Snap();
            r.Player.Raise(RtspPlayerEvent.Playing); Snap();
            r.Player.Raise(RtspPlayerEvent.Dropped); Snap();
            foreach (var kind in Enum.GetValues<RtspFailure>())
            {
                r.Vm.StopCommand.Execute(null);
                r.Vm.StartCommand.Execute(null);
                r.Player.Raise(RtspPlayerEvent.Failed(kind, FakeUrl)); Snap();
            }
            r.Vm.StopCommand.Execute(null); Snap();
            foreach (var (status, masked) in seen)
                foreach (var text in new[] { status, masked })
                {
                    Assert.DoesNotContain("pass-fake", text);
                    Assert.DoesNotContain("abc", text);
                    Assert.DoesNotContain("user:", text);
                }
            foreach (var (status, _) in seen)
                Assert.DoesNotContain("example.test", status);
        }

        [Fact]
        public void PlayerFailureMessage_WithUrl_IsMasked()
        {
            var r = new Rig();
            r.Vm.StartCommand.Execute(null);
            r.Player.Raise(RtspPlayerEvent.Failed(RtspFailure.Other, "open failed for " + FakeUrl));
            Assert.Equal(RtspTileState.Error, r.Vm.State);
            Assert.DoesNotContain("pass-fake", r.Vm.StatusText);
            Assert.DoesNotContain("token=abc", r.Vm.StatusText);
            Assert.DoesNotContain("user:", r.Vm.StatusText);
        }

        [Fact]
        public void RetryPolicy_Sequence()
        {
            Assert.Equal(new[] { 1, 2, 5, 10, 10, 10 },
                new[] { 1, 2, 3, 4, 5, 6 }.Select(a => (int)RtspRetryPolicy.NextDelay(a).TotalSeconds).ToArray());
        }
    }
}
