using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Services.Visca;
using AvasRoutingTool.Tests.Fakes;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>Poll and link-state tests of ViscaCamera. Motion tests are in ViscaCameraTests.cs.</summary>
    public partial class ViscaCameraTests
    {
        [Theory]
        [InlineData(ViscaTransportKind.Udp)]
        [InlineData(ViscaTransportKind.Tcp)]
        public async Task Poll_ReadsPowerZoomFocus(ViscaTransportKind kind)
        {
            using var server = new FakeViscaServer(kind);
            server.ZoomPosition = 0x2A5C;
            server.FocusPosition = 0x1234;
            server.AutoFocus = true;
            await using var camera = Create(server, kind);
            await camera.ConnectAsync(CancellationToken.None);

            CameraStatus? on = await camera.PollAsync(CancellationToken.None);
            Assert.Equal(new CameraStatus(CameraPower.On, 0x2A5C, 0x1234, true), on);

            server.PowerOn = false;
            server.AutoFocus = false;
            CameraStatus? off = await camera.PollAsync(CancellationToken.None);
            Assert.Equal(new CameraStatus(CameraPower.Off, 0x2A5C, 0x1234, false), off);
        }

        [Theory]
        [InlineData(ViscaTransportKind.Udp)]
        [InlineData(ViscaTransportKind.Tcp)]
        public async Task Poll_NoAnswer_ReturnsNull_AndSetsUnreachableAfter3(ViscaTransportKind kind)
        {
            using var server = new FakeViscaServer(kind);
            await using var camera = Create(server, kind);
            await camera.ConnectAsync(CancellationToken.None);
            server.Responder = _ => Array.Empty<byte[]>();
            camera.AckTimeout = TimeSpan.FromMilliseconds(100);

            Assert.Null(await camera.PollAsync(CancellationToken.None));
            Assert.Null(await camera.PollAsync(CancellationToken.None));
            Assert.Equal(CameraLinkState.Connected, camera.LinkState);
            Assert.Null(await camera.PollAsync(CancellationToken.None));
            Assert.Equal(CameraLinkState.Unreachable, camera.LinkState);

            // Any success resets.
            server.Responder = msg => msg[1] == 0x09 ? new[] { PowerOnReply() } : new[] { Ack, Completion };
            Assert.True(await camera.SetPowerAsync(true, CancellationToken.None));
            Assert.Equal(CameraLinkState.Connected, camera.LinkState);
        }

        [Fact]
        public async Task ConnectFailure_SetsConnecting_ThenUnreachable_BackoffDoublesTo30s()
        {
            var clock = new TestClock();
            var stub = new StubTransports { FailConnect = true };
            var states = new List<CameraLinkState>();
            await using var camera = new ViscaCamera(new RoomCameraConfig(), clock.Func, stub.Factory);
            camera.LinkStateChanged += states.Add;
            DateTime t0 = clock.Now;

            await camera.ConnectAsync(CancellationToken.None);
            Assert.Equal(CameraLinkState.Connecting, camera.LinkState);
            Assert.Equal(t0 + TimeSpan.FromSeconds(1), camera.NextConnectAttempt);

            // Not due yet: Tick does nothing.
            clock.Advance(TimeSpan.FromMilliseconds(900));
            camera.Tick();
            await Task.Delay(100);
            Assert.Equal(1, stub.ConnectAttempts);

            int attempts = 1;
            DateTime due = t0 + TimeSpan.FromSeconds(1);
            foreach (int delay in new[] { 2, 4, 8, 16, 30, 30 })
            {
                clock.Advance(due - clock.Now);
                camera.Tick();
                attempts++;
                DateTime expectedNext = clock.Now + TimeSpan.FromSeconds(delay);
                int expectedAttempts = attempts;
                Assert.True(
                    await EventuallyAsync(() => stub.ConnectAttempts == expectedAttempts && camera.NextConnectAttempt == expectedNext),
                    $"attempt {attempts}: expected the next one at +{delay}s");
                due = expectedNext;
                // The third failed attempt in a row is Unreachable, and it stays so.
                Assert.Equal(attempts >= 3 ? CameraLinkState.Unreachable : CameraLinkState.Connecting, camera.LinkState);
            }

            // The camera comes back: the next due Tick connects and resets.
            stub.FailConnect = false;
            clock.Advance(due - clock.Now);
            camera.Tick();
            Assert.True(await EventuallyAsync(() => camera.LinkState == CameraLinkState.Connected));
            Assert.Equal(new[] { CameraLinkState.Connecting, CameraLinkState.Unreachable, CameraLinkState.Connected }, states);
        }

        [Fact]
        public async Task ServerDisconnect_MovesToDisconnected_AndReconnects()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Tcp);
            await using var camera = Create(server, ViscaTransportKind.Tcp);
            await camera.ConnectAsync(CancellationToken.None);
            Assert.Equal(CameraLinkState.Connected, camera.LinkState);

            server.Disconnect();
            Assert.True(await EventuallyAsync(() => camera.LinkState == CameraLinkState.Disconnected));
            Assert.False(await camera.SetPowerAsync(true, CancellationToken.None));

            camera.Tick();
            Assert.True(await EventuallyAsync(() => camera.LinkState == CameraLinkState.Connected));
            Assert.NotNull(await camera.PollAsync(CancellationToken.None));
        }

        [Fact]
        public async Task TransportDisconnected_WithReset10054_MovesToDisconnected_ThenRetriesViaTick()
        {
            // UDP raises Disconnected with SocketException 10054 when the camera is switched off.
            var clock = new TestClock();
            var stub = new StubTransports();
            await using var camera = new ViscaCamera(new RoomCameraConfig(), clock.Func, stub.Factory);
            await camera.ConnectAsync(CancellationToken.None);
            Assert.Equal(CameraLinkState.Connected, camera.LinkState);

            stub.Transports[0].RaiseDisconnected(new SocketException(10054));
            Assert.Equal(CameraLinkState.Disconnected, camera.LinkState);
            Assert.True(await EventuallyAsync(() => stub.Transports[0].Disposed));

            stub.FailConnect = true;
            camera.Tick();
            DateTime retryAt = clock.Now + TimeSpan.FromSeconds(1);
            Assert.True(await EventuallyAsync(() => stub.ConnectAttempts == 2 && camera.NextConnectAttempt == retryAt && camera.LinkState == CameraLinkState.Connecting));

            stub.FailConnect = false;
            clock.Advance(TimeSpan.FromSeconds(1));
            camera.Tick();
            Assert.True(await EventuallyAsync(() => camera.LinkState == CameraLinkState.Connected));
        }

        [Fact]
        public async Task Tick_WithoutConnectAsync_DoesNothing()
        {
            var stub = new StubTransports();
            await using var camera = new ViscaCamera(new RoomCameraConfig(), new TestClock().Func, stub.Factory);
            camera.Tick();
            await Task.Delay(50);
            Assert.Equal(0, stub.ConnectAttempts);
            Assert.Equal(CameraLinkState.Disconnected, camera.LinkState);
        }

        [Fact]
        public async Task Commands_AfterDispose_ReturnFalse_AndOtherCallsAreSafe()
        {
            var stub = new StubTransports();
            var camera = new ViscaCamera(new RoomCameraConfig(), new TestClock().Func, stub.Factory);
            await camera.ConnectAsync(CancellationToken.None);
            await camera.DisposeAsync();

            Assert.True(stub.Transports[0].Disposed);
            Assert.False(await camera.HomeAsync(CancellationToken.None));
            Assert.False(await camera.StopMoveAsync(CancellationToken.None));
            Assert.Null(await camera.PollAsync(CancellationToken.None));
            camera.StopImmediately();
            camera.Tick();
            await camera.ConnectAsync(CancellationToken.None);
            Assert.Equal(1, stub.ConnectAttempts);
        }

        /// <summary>Scriptable transports for link-state tests.</summary>
        internal sealed class StubTransports
        {
            public bool FailConnect { get; set; }
            public int ConnectAttempts;
            public List<StubTransport> Transports { get; } = new();

            public IViscaTransport Factory(RoomCameraConfig _)
            {
                var transport = new StubTransport(this);
                lock (Transports) Transports.Add(transport);
                return transport;
            }
        }

        internal sealed class StubTransport : IViscaTransport
        {
            private readonly StubTransports _owner;

            public StubTransport(StubTransports owner)
            {
                _owner = owner;
            }

            public bool IsConnected { get; private set; }
            public bool Disposed { get; private set; }
#pragma warning disable CS0067
            public event Action<byte[]>? MessageReceived;
#pragma warning restore CS0067
            public event Action<Exception?>? Disconnected;

            public Task ConnectAsync(CancellationToken ct)
            {
                Interlocked.Increment(ref _owner.ConnectAttempts);
                if (_owner.FailConnect)
                    throw new SocketException(10061);
                IsConnected = true;
                return Task.CompletedTask;
            }

            public Task SendAsync(byte[] visca, bool isInquiry, CancellationToken ct) => Task.CompletedTask;

            public void RaiseDisconnected(Exception? cause)
            {
                IsConnected = false;
                Disconnected?.Invoke(cause);
            }

            public ValueTask DisposeAsync()
            {
                Disposed = true;
                IsConnected = false;
                return ValueTask.CompletedTask;
            }
        }
    }
}
