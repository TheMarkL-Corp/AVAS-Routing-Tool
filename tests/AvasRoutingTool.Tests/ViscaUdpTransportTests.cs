using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    public class ViscaUdpTransportTests
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

        private static UdpViscaTransport Create(FakeViscaServer server, int localPort = 0) =>
            new UdpViscaTransport("127.0.0.1", server.Port, localPort);

        // A local UDP port that is free right now (bound, read, released).
        private static int FreePort()
        {
            using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        }

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
        public async Task Connect_SendsSequenceReset_AndFirstCommandHasSequence1()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Udp);
            await using var transport = Create(server);
            var seen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            server.CommandReceived += _ => seen.TrySetResult(true);

            await transport.ConnectAsync(CancellationToken.None);
            await transport.SendAsync(ViscaCommands.Home(1), isInquiry: false, CancellationToken.None);

            await seen.Task.WaitAsync(Wait);
            Assert.Equal(1, server.SequenceResetCount);
            Assert.Equal(new uint[] { 1 }, server.ReceivedSequences);
        }

        [Fact]
        public async Task SequenceNumbers_Increase_PerMessage()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Udp);
            await using var transport = Create(server);
            await transport.ConnectAsync(CancellationToken.None);

            await transport.SendAsync(ViscaCommands.Home(1), isInquiry: false, CancellationToken.None);
            await transport.SendAsync(ViscaCommands.Power(1, on: true), isInquiry: false, CancellationToken.None);
            await transport.SendAsync(ViscaCommands.InquirePower(1), isInquiry: true, CancellationToken.None);

            await WaitUntilAsync(() => server.Received.Count == 3);
            Assert.Equal(new uint[] { 1, 2, 3 }, server.ReceivedSequences);
        }

        [Fact]
        public async Task Inquiry_IsSentWithInquiryPayloadType_CommandWithCommandType()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Udp);
            await using var transport = Create(server);
            await transport.ConnectAsync(CancellationToken.None);

            await transport.SendAsync(ViscaCommands.Power(1, on: true), isInquiry: false, CancellationToken.None);
            await transport.SendAsync(ViscaCommands.InquirePower(1), isInquiry: true, CancellationToken.None);

            await WaitUntilAsync(() => server.Received.Count == 2);
            Assert.Equal(new[] { ViscaPayloadType.Command, ViscaPayloadType.Inquiry }, server.ReceivedTypes);
        }

        [Fact]
        public async Task ReplyWithOtherSequence_IsStillDelivered()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Udp);
            server.ReplySequenceFor = sequence => sequence + 100;
            await using var transport = Create(server);
            var inbox = new Inbox();
            transport.MessageReceived += inbox.Add;
            await transport.ConnectAsync(CancellationToken.None);

            await transport.SendAsync(ViscaCommands.Home(1), isInquiry: false, CancellationToken.None);

            await inbox.Count2.Task.WaitAsync(Wait);
            Assert.Equal(new[] { Hex("90 41 FF"), Hex("90 51 FF") }, inbox.Messages);
        }

        [Fact]
        public async Task LostDatagram_IsNotRetriedByTransport_AndRaisesNoDisconnect()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Udp);
            await using var transport = Create(server);
            var inbox = new Inbox();
            transport.MessageReceived += inbox.Add;
            int disconnected = 0;
            transport.Disconnected += _ => Interlocked.Increment(ref disconnected);
            await transport.ConnectAsync(CancellationToken.None);

            server.DropNext(3);
            // UDP send succeeds even though the camera never sees the datagram.
            await transport.SendAsync(ViscaCommands.Home(1), isInquiry: false, CancellationToken.None);

            await WaitUntilAsync(() => server.DroppedCount == 1);
            await Task.Delay(300);   // Give a hypothetical retry time to arrive.
            Assert.Equal(1, server.DroppedCount);
            Assert.Empty(server.Received);
            lock (inbox.Messages) Assert.Empty(inbox.Messages);
            Assert.True(transport.IsConnected);
            Assert.Equal(0, Volatile.Read(ref disconnected));
        }

        [Fact]
        public async Task Dispose_ReleasesLocalPort()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Udp);
            int port = FreePort();
            var transport = Create(server, port);
            await transport.ConnectAsync(CancellationToken.None);

            await transport.DisposeAsync();

            // The port can be bound again only if the transport closed its socket.
            using var rebound = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            Assert.Equal(port, ((IPEndPoint)rebound.Client.LocalEndPoint!).Port);
        }

        [Fact]
        public async Task ConnectDisposeLoop_200Times_DoesNotGrowHandles()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Udp);

            async Task ConnectAndDisposeAsync()
            {
                var transport = Create(server);
                await transport.ConnectAsync(CancellationToken.None);
                await transport.SendAsync(ViscaCommands.InquirePower(1), isInquiry: true, CancellationToken.None);
                await transport.DisposeAsync();
            }

            // Warm up so lazily created runtime handles are not counted.
            for (int i = 0; i < 5; i++) await ConnectAndDisposeAsync();
            CollectGarbage();
            int before = Process.GetCurrentProcess().HandleCount;

            for (int i = 0; i < 200; i++) await ConnectAndDisposeAsync();
            CollectGarbage();
            int after = Process.GetCurrentProcess().HandleCount;

            Assert.True(after - before < 50, $"Handle count grew from {before} to {after}.");
        }

        [Fact]
        public async Task Datagram_FromOtherHost_IsIgnored()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Udp);
            int port = FreePort();
            await using var transport = Create(server, port);
            var inbox = new Inbox();
            transport.MessageReceived += inbox.Add;
            await transport.ConnectAsync(CancellationToken.None);

            // A reply that arrives from a different endpoint than the connected camera.
            server.SendDatagramFromOtherEndpoint(ViscaFrame.Encode(ViscaPayloadType.Reply, 1, Hex("90 60 02 FF")), port);

            await transport.SendAsync(ViscaCommands.Home(1), isInquiry: false, CancellationToken.None);

            await inbox.Count2.Task.WaitAsync(Wait);
            Assert.Equal(new[] { Hex("90 41 FF"), Hex("90 51 FF") }, inbox.Messages);
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + Wait;
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("Condition not reached in time.");
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
