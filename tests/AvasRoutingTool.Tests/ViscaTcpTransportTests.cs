using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    public class ViscaTcpTransportTests
    {
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

        private static byte[] Hex(string hex)
        {
            var parts = hex.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                bytes[i] = byte.Parse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }

        private static TcpViscaTransport Create(FakeViscaServer server) =>
            new TcpViscaTransport("127.0.0.1", server.Port);

        // Collects the messages a transport raises, in order.
        private sealed class Inbox
        {
            public List<byte[]> Messages { get; } = new();
            public TaskCompletionSource<bool> Count2 { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public void Add(byte[] message)
            {
                lock (Messages)
                {
                    Messages.Add(message);
                    if (Messages.Count >= 2) Count2.TrySetResult(true);
                }
            }
        }

        [Fact]
        public async Task Send_DeliversBytesToServer()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Tcp);
            await using var transport = Create(server);
            await transport.ConnectAsync(CancellationToken.None);

            var command = ViscaCommands.Power(1, on: true);
            var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            server.CommandReceived += msg => received.TrySetResult(msg);

            await transport.SendAsync(command, isInquiry: false, CancellationToken.None);

            var bytes = await received.Task.WaitAsync(Wait);
            Assert.Equal(command, bytes);
        }

        [Fact]
        public async Task ServerReply_RaisesMessageReceived()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Tcp);
            await using var transport = Create(server);
            var inbox = new Inbox();
            transport.MessageReceived += inbox.Add;
            await transport.ConnectAsync(CancellationToken.None);

            // The default responder ACKs a command and then reports completion.
            await transport.SendAsync(ViscaCommands.Home(1), isInquiry: false, CancellationToken.None);

            await inbox.Count2.Task.WaitAsync(Wait);
            Assert.Equal(new[] { Hex("90 41 FF"), Hex("90 51 FF") }, inbox.Messages);
        }

        [Fact]
        public async Task ServerClose_RaisesDisconnected()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Tcp);
            await using var transport = Create(server);
            var disconnected = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
            transport.Disconnected += ex => disconnected.TrySetResult(ex);
            await transport.ConnectAsync(CancellationToken.None);
            Assert.True(transport.IsConnected);

            server.Disconnect();

            await disconnected.Task.WaitAsync(Wait);
            Assert.False(transport.IsConnected);
        }

        [Fact]
        public async Task Connect_ToClosedPort_ThrowsOrFailsWithinTimeout()
        {
            int closedPort;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            closedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            await using var transport = new TcpViscaTransport("127.0.0.1", closedPort);
            var sw = Stopwatch.StartNew();

            var ex = await Record.ExceptionAsync(() => transport.ConnectAsync(CancellationToken.None));

            // This machine takes about 2 s to refuse a loopback connect, so the transport's own
            // ConnectTimeout may fire first; either way the attempt must fail within that bound.
            Assert.NotNull(ex);
            Assert.True(sw.Elapsed < TcpViscaTransport.ConnectTimeout + TimeSpan.FromSeconds(1), $"Connect took {sw.Elapsed}.");
            Assert.False(transport.IsConnected);
        }

        [Fact]
        public async Task ConnectAsync_HonoursCancellationToken()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Tcp);
            await using var transport = Create(server);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.ConnectAsync(cts.Token));
            Assert.False(transport.IsConnected);
        }

        [Fact]
        public async Task Dispose_ReleasesSocket()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Tcp);
            var first = Create(server);
            await first.ConnectAsync(CancellationToken.None);
            var serverSawClose = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            server.ClientDisconnected += () => serverSawClose.TrySetResult(true);

            await first.DisposeAsync();

            // The fake server's accepted socket reads end-of-stream once the client has closed.
            await serverSawClose.Task.WaitAsync(Wait);

            // The server still accepts, so a second transport connects normally.
            await using var second = Create(server);
            await second.ConnectAsync(CancellationToken.None);
            Assert.True(second.IsConnected);
        }

        [Fact]
        public async Task Dispose_DoesNotRaiseDisconnected()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Tcp);
            var transport = Create(server);
            int raised = 0;
            transport.Disconnected += _ => Interlocked.Increment(ref raised);
            await transport.ConnectAsync(CancellationToken.None);

            await transport.DisposeAsync();

            Assert.Equal(0, Volatile.Read(ref raised));
        }

        [Fact]
        public async Task ConnectDisposeLoop_200Times_DoesNotGrowHandles()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Tcp);

            async Task ConnectAndDisposeAsync()
            {
                var transport = Create(server);
                await transport.ConnectAsync(CancellationToken.None);
                await transport.SendAsync(ViscaCommands.InquirePower(1), isInquiry: true, CancellationToken.None);
                await transport.DisposeAsync();
            }

            // Warm up so lazily created runtime handles are not counted.
            for (int i = 0; i < 5; i++) await ConnectAndDisposeAsync();
            await WaitForServerClientsAsync(server, 0);
            CollectGarbage();
            int before = Process.GetCurrentProcess().HandleCount;

            for (int i = 0; i < 200; i++) await ConnectAndDisposeAsync();
            await WaitForServerClientsAsync(server, 0);
            CollectGarbage();
            int after = Process.GetCurrentProcess().HandleCount;

            Assert.True(after - before < 50, $"Handle count grew from {before} to {after}.");
        }

        [Fact]
        public async Task Dispose_WhileSendPending_DoesNotThrowOrHang()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Tcp);
            var transport = Create(server);
            int raised = 0;
            transport.Disconnected += _ => Interlocked.Increment(ref raised);
            await transport.ConnectAsync(CancellationToken.None);

            // Larger than the socket buffers, so the write is likely still pending at dispose.
            var big = new byte[16 * 1024 * 1024];
            Array.Fill(big, (byte)0x11);
            Task send = transport.SendAsync(big, isInquiry: false, CancellationToken.None);

            await transport.DisposeAsync().AsTask().WaitAsync(Wait);

            // The send ends in any state (completed, or failed because the socket closed); it must not hang.
            var finished = await Task.WhenAny(send, Task.Delay(Wait));
            Assert.Same(send, finished);
            Assert.Equal(0, Volatile.Read(ref raised));
        }

        [Fact]
        public async Task DropNext_SilentlyIgnoresCommand_ThenAnswersNext()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Tcp);
            await using var transport = Create(server);
            var inbox = new Inbox();
            transport.MessageReceived += inbox.Add;
            await transport.ConnectAsync(CancellationToken.None);

            server.DropNext(1);
            await transport.SendAsync(ViscaCommands.Home(1), isInquiry: false, CancellationToken.None);
            var second = ViscaCommands.Power(1, on: true);
            await transport.SendAsync(second, isInquiry: false, CancellationToken.None);

            await inbox.Count2.Task.WaitAsync(Wait);
            Assert.Equal(new[] { Hex("90 41 FF"), Hex("90 51 FF") }, inbox.Messages);
            Assert.Equal(new[] { second }, server.Received);
        }

        private static async Task WaitForServerClientsAsync(FakeViscaServer server, int expected)
        {
            var deadline = DateTime.UtcNow + Wait;
            while (server.OpenClientCount != expected)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Server still has {server.OpenClientCount} open clients.");
                await Task.Delay(5);
            }
        }

        private static void CollectGarbage()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
