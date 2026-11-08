using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Services.Visca;
using AvasRoutingTool.Tests.Fakes;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>Motion, stop and dispose behaviour of ViscaCamera against FakeViscaServer. Link and poll tests are in ViscaCameraTests.Link.cs.</summary>
    public partial class ViscaCameraTests
    {
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

        private static readonly byte[] Ack = { 0x90, 0x41, 0xFF };
        private static readonly byte[] Completion = { 0x90, 0x51, 0xFF };
        private static readonly byte[] DriveCancelled = { 0x90, 0x61, 0x04, 0xFF };

        [Theory]
        [InlineData(ViscaTransportKind.Udp)]
        [InlineData(ViscaTransportKind.Tcp)]
        public async Task Move_ThenStop_ServerSeesDriveThenStop(ViscaTransportKind kind)
        {
            using var server = new FakeViscaServer(kind);
            var log = new CommandLog(server);
            await using var camera = Create(server, kind);
            await camera.ConnectAsync(CancellationToken.None);
            log.Clear();

            bool moved = await camera.MoveAsync(PanTiltDirection.UpLeft, new PtzSpeed(0x0C, 0x0A, 4), CancellationToken.None);
            bool stopped = await camera.StopMoveAsync(CancellationToken.None);

            Assert.True(moved);
            Assert.True(stopped);
            Assert.Equal(
                new[]
                {
                    ViscaHex.Of(ViscaCommands.PanTilt(1, PanTiltDirection.UpLeft, 0x0C, 0x0A)),
                    ViscaHex.Of(ViscaCommands.StopPanTilt(1))
                },
                log.Commands());
        }

        [Theory]
        [InlineData(ViscaTransportKind.Udp)]
        [InlineData(ViscaTransportKind.Tcp)]
        public async Task Stop_JumpsTheQueue_WhenMoveIsAwaitingCompletion(ViscaTransportKind kind)
        {
            using var server = new FakeViscaServer(kind);
            var log = new CommandLog(server);
            // Drives are acknowledged but not completed; a stop is acknowledged, cancels the drive, then completes.
            server.Responder = msg => msg[1] == 0x09 ? new[] { PowerOnReply() }
                : IsPanTiltStop(msg) ? new[] { Ack, DriveCancelled, Completion }
                : new[] { Ack };
            await using var camera = Create(server, kind);
            await camera.ConnectAsync(CancellationToken.None);

            Task<bool> move = camera.MoveAsync(PanTiltDirection.Left, new PtzSpeed(5, 4, 2), CancellationToken.None);
            Assert.True(await EventuallyAsync(() => log.Commands().Count == 1));
            await Task.Delay(50);
            Assert.False(move.IsCompleted);

            Task<bool> stop = camera.StopMoveAsync(CancellationToken.None);

            Assert.False(await move.WaitAsync(Wait));
            Assert.True(await stop.WaitAsync(Wait));
            Assert.Equal(
                new[]
                {
                    ViscaHex.Of(ViscaCommands.PanTilt(1, PanTiltDirection.Left, 5, 4)),
                    ViscaHex.Of(ViscaCommands.StopPanTilt(1))
                },
                log.Commands());
        }

        [Theory]
        [InlineData(ViscaTransportKind.Udp)]
        [InlineData(ViscaTransportKind.Tcp)]
        public async Task QueuedMove_IssuedBeforeStop_IsNeverSentAfterStop(ViscaTransportKind kind)
        {
            using var server = new FakeViscaServer(kind);
            var log = new CommandLog(server);
            // Drives are acknowledged but never completed, so the first one holds the queue until a Stop.
            server.Responder = msg => msg[1] == 0x09 ? new[] { PowerOnReply() }
                : IsPanTiltStop(msg) ? new[] { Ack, DriveCancelled, Completion }
                : new[] { Ack };
            await using var camera = Create(server, kind);
            await camera.ConnectAsync(CancellationToken.None);

            Task<bool> moveA = camera.MoveAsync(PanTiltDirection.Left, new PtzSpeed(5, 4, 2), CancellationToken.None);
            Assert.True(await EventuallyAsync(() => log.Commands().Count == 1));
            Task<bool> moveB = camera.MoveAsync(PanTiltDirection.Right, new PtzSpeed(6, 6, 2), CancellationToken.None);
            await Task.Delay(50);   // moveB is now waiting for the queue
            Task<bool> stop = camera.StopMoveAsync(CancellationToken.None);

            Assert.False(await moveA.WaitAsync(Wait));
            Assert.False(await moveB.WaitAsync(Wait));
            Assert.True(await stop.WaitAsync(Wait));
            Assert.Equal(
                new[]
                {
                    ViscaHex.Of(ViscaCommands.PanTilt(1, PanTiltDirection.Left, 5, 4)),
                    ViscaHex.Of(ViscaCommands.StopPanTilt(1))
                },
                log.Commands());
        }

        [Theory]
        [InlineData(ViscaTransportKind.Udp)]
        [InlineData(ViscaTransportKind.Tcp)]
        public async Task QueuedZoom_IssuedBeforeStop_IsNeverSentAfterStop(ViscaTransportKind kind)
        {
            using var server = new FakeViscaServer(kind);
            var log = new CommandLog(server);
            // Zoom drives are acknowledged but never completed, so the first one holds the queue until a Stop.
            server.Responder = msg => msg[1] == 0x09 ? new[] { PowerOnReply() }
                : IsZoomStop(msg) ? new[] { Ack, Completion }
                : new[] { Ack };
            await using var camera = Create(server, kind);
            await camera.ConnectAsync(CancellationToken.None);

            Task<bool> zoomA = camera.ZoomAsync(ZoomDirection.Tele, new PtzSpeed(5, 4, 2), CancellationToken.None);
            Assert.True(await EventuallyAsync(() => log.Commands().Count == 1));
            Task<bool> zoomB = camera.ZoomAsync(ZoomDirection.Wide, new PtzSpeed(6, 6, 3), CancellationToken.None);
            await Task.Delay(50);   // zoomB is now waiting for the queue
            Task<bool> stop = camera.StopZoomAsync(CancellationToken.None);

            Assert.False(await zoomA.WaitAsync(Wait));
            Assert.False(await zoomB.WaitAsync(Wait));
            Assert.True(await stop.WaitAsync(Wait));
            Assert.Equal(
                new[]
                {
                    ViscaHex.Of(ViscaCommands.Zoom(1, ZoomDirection.Tele, 2)),
                    ViscaHex.Of(ViscaCommands.StopZoom(1))
                },
                log.Commands());
        }

        [Theory]
        [InlineData(ViscaTransportKind.Udp)]
        [InlineData(ViscaTransportKind.Tcp)]
        public async Task CommandWithoutAck_RetriesTwice_ThenReturnsFalse(ViscaTransportKind kind)
        {
            using var server = new FakeViscaServer(kind);
            var log = new CommandLog(server);
            await using var camera = Create(server, kind);
            await camera.ConnectAsync(CancellationToken.None);
            server.Responder = _ => Array.Empty<byte[]>();
            camera.AckTimeout = TimeSpan.FromMilliseconds(100);
            log.Clear();

            bool result = await camera.SetZoomPositionAsync(0x2A5C, CancellationToken.None);

            Assert.False(result);
            Assert.Equal(3, log.Commands().Count);   // one send and two retries
        }

        [Theory]
        [InlineData(ViscaTransportKind.Udp)]
        [InlineData(ViscaTransportKind.Tcp)]
        public async Task Stop_WithoutAck_RetriesThreeTimes_ThenReturnsFalse(ViscaTransportKind kind)
        {
            using var server = new FakeViscaServer(kind);
            var log = new CommandLog(server);
            await using var camera = Create(server, kind);
            await camera.ConnectAsync(CancellationToken.None);
            server.Responder = _ => Array.Empty<byte[]>();
            camera.AckTimeout = TimeSpan.FromMilliseconds(100);
            log.Clear();

            bool result = await camera.StopMoveAsync(CancellationToken.None);

            Assert.False(result);
            Assert.Equal(4, log.Commands().Count);   // one send and three retries
            Assert.All(log.Commands(), hex => Assert.Equal(ViscaHex.Of(ViscaCommands.StopPanTilt(1)), hex));
        }

        [Theory]
        [InlineData(ViscaTransportKind.Udp)]
        [InlineData(ViscaTransportKind.Tcp)]
        public async Task StopImmediately_SendsStopWithoutAwait(ViscaTransportKind kind)
        {
            using var server = new FakeViscaServer(kind);
            var log = new CommandLog(server);
            await using var camera = Create(server, kind);
            await camera.ConnectAsync(CancellationToken.None);
            log.Clear();

            var clock = Stopwatch.StartNew();
            camera.StopImmediately();
            Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(500));

            Assert.True(await EventuallyAsync(() => log.Commands().Contains(ViscaHex.Of(ViscaCommands.StopPanTilt(1)))));
        }

        [Theory]
        [InlineData(ViscaTransportKind.Udp)]
        [InlineData(ViscaTransportKind.Tcp)]
        public async Task DisposeTwice_IsSafe(ViscaTransportKind kind)
        {
            using var server = new FakeViscaServer(kind);
            var camera = Create(server, kind);
            await camera.ConnectAsync(CancellationToken.None);

            await camera.DisposeAsync();
            await camera.DisposeAsync();
        }

        [Theory]
        [InlineData(ViscaTransportKind.Udp)]
        [InlineData(ViscaTransportKind.Tcp)]
        public async Task Dispose_ReleasesAllSockets(ViscaTransportKind kind)
        {
            using var server = new FakeViscaServer(kind);
            int localPort = FreeUdpPort();
            Func<RoomCameraConfig, IViscaTransport>? factory = kind == ViscaTransportKind.Udp
                ? _ => new UdpViscaTransport("127.0.0.1", server.Port, localPort)
                : null;
            var camera = new ViscaCamera(Config(server, kind), new TestClock().Func, factory);
            await camera.ConnectAsync(CancellationToken.None);

            await camera.DisposeAsync();

            if (kind == ViscaTransportKind.Udp)
            {
                // The camera's own local port must be free again.
                using var rebound = new UdpClient(localPort);
            }
            else
            {
                Assert.True(await EventuallyAsync(() => server.OpenClientCount == 0));
            }
        }

        // ---- helpers shared with ViscaCameraTests.Link.cs ----

        internal static RoomCameraConfig Config(FakeViscaServer server, ViscaTransportKind kind) => new()
        {
            Host = "127.0.0.1",
            Port = server.Port,
            Transport = kind,
            ViscaAddress = 1
        };

        internal static ViscaCamera Create(FakeViscaServer server, ViscaTransportKind kind, TestClock? clock = null)
            => new(Config(server, kind), (clock ?? new TestClock()).Func);

        internal static byte[] PowerOnReply() => new byte[] { 0x90, 0x50, 0x02, 0xFF };

        private static bool IsPanTiltStop(byte[] msg) => msg.Length == 9 && msg[6] == 0x03 && msg[7] == 0x03;

        private static bool IsZoomStop(byte[] msg) => msg.Length == 6 && msg[3] == 0x07 && msg[4] == 0x00;

        // Polls a condition until it holds or the wait runs out.
        internal static async Task<bool> EventuallyAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + Wait;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return true;
                await Task.Delay(10);
            }
            return condition();
        }

        private static int FreeUdpPort()
        {
            using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        }

        /// <summary>A clock the test moves by hand.</summary>
        internal sealed class TestClock
        {
            public DateTime Now { get; private set; } = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

            public Func<DateTime> Func => () => Now;

            public void Advance(TimeSpan by) => Now += by;
        }

        /// <summary>Records every VISCA message the fake camera receives, in arrival order.</summary>
        internal sealed class CommandLog
        {
            private readonly ConcurrentQueue<byte[]> _messages = new();

            public CommandLog(FakeViscaServer server)
            {
                server.CommandReceived += msg => _messages.Enqueue(msg);
            }

            /// <summary>Hex of every non-inquiry message (drive, stop, power, ...), in arrival order.</summary>
            public List<string> Commands() => _messages.Where(m => m[1] != 0x09).Select(ViscaHex.Of).ToList();

            public void Clear() => _messages.Clear();
        }
    }

    internal static class ViscaHex
    {
        public static string Of(byte[] bytes) => string.Join(' ', bytes.Select(b => b.ToString("X2")));
    }
}
