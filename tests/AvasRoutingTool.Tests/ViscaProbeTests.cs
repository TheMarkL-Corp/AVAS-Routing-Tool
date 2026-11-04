using System;
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
    public class ViscaProbeTests
    {
        private const string Host = "127.0.0.1";
        private static readonly TimeSpan PerProbe = TimeSpan.FromSeconds(1);

        // A reply that is not VISCA: wrong header nibble, terminated.
        private static readonly byte[] Garbage = { 0x12, 0x34, 0xFF };

        // A local port that is free right now (bound, read, released).
        private static int FreePort()
        {
            using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        }

        private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan limit)
        {
            var deadline = DateTime.UtcNow + limit;
            while (!condition() && DateTime.UtcNow < deadline)
                await Task.Delay(20);
        }

        [Fact]
        public async Task FindsTcpCameraOnCustomPort()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Tcp);

            var hits = await ViscaProbe.DetectAsync(Host, new[] { (ViscaTransportKind.Tcp, server.Port) }, PerProbe, CancellationToken.None);

            ProbeHit hit = Assert.Single(hits);
            Assert.Equal(ViscaTransportKind.Tcp, hit.Transport);
            Assert.Equal(server.Port, hit.Port);
            Assert.Equal(CameraPower.On, hit.Power);
        }

        [Fact]
        public async Task FindsUdpCameraOnCustomPort()
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Udp);

            var hits = await ViscaProbe.DetectAsync(Host, new[] { (ViscaTransportKind.Udp, server.Port) }, PerProbe, CancellationToken.None);

            ProbeHit hit = Assert.Single(hits);
            Assert.Equal(ViscaTransportKind.Udp, hit.Transport);
            Assert.Equal(server.Port, hit.Port);
            Assert.Equal(CameraPower.On, hit.Power);
        }

        [Fact]
        public async Task NothingListening_ReturnsEmpty_WithinTimeoutBudget()
        {
            var candidates = new List<(ViscaTransportKind, int)>();
            for (int i = 0; i < 3; i++)
            {
                candidates.Add((ViscaTransportKind.Tcp, FreePort()));
                candidates.Add((ViscaTransportKind.Udp, FreePort()));
            }

            var clock = Stopwatch.StartNew();
            var hits = await ViscaProbe.DetectAsync(Host, candidates, PerProbe, CancellationToken.None);
            clock.Stop();

            Assert.Empty(hits);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"Probing six candidates took {clock.Elapsed}.");
        }

        [Theory]
        [InlineData(ViscaTransportKind.Tcp)]
        [InlineData(ViscaTransportKind.Udp)]
        public async Task PortThatAcceptsButNeverReplies_IsNotAHit(ViscaTransportKind kind)
        {
            using var server = new FakeViscaServer(kind) { Responder = _ => Array.Empty<byte[]>() };

            var hits = await ViscaProbe.DetectAsync(Host, new[] { (kind, server.Port) }, TimeSpan.FromMilliseconds(500), CancellationToken.None);

            Assert.Empty(hits);
        }

        [Fact]
        public async Task Cancellation_StopsAndDisposesEverySocket()
        {
            using var tcp = new FakeViscaServer(ViscaTransportKind.Tcp) { Responder = _ => Array.Empty<byte[]>() };
            using var udp = new FakeViscaServer(ViscaTransportKind.Udp) { Responder = _ => Array.Empty<byte[]>() };
            var candidates = new[] { (ViscaTransportKind.Tcp, tcp.Port), (ViscaTransportKind.Udp, udp.Port) };
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            Task<IReadOnlyList<ProbeHit>> task = ViscaProbe.DetectAsync(Host, candidates, TimeSpan.FromSeconds(30), cts.Token);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

            // The probe's TCP client has left: the camera side sees its connection close.
            await WaitUntilAsync(() => tcp.OpenClientCount == 0, TimeSpan.FromSeconds(2));
            Assert.Equal(0, tcp.OpenClientCount);

            // Every port the probe touched can be bound again.
            int tcpPort = tcp.Port;
            int udpPort = udp.Port;
            tcp.Dispose();
            udp.Dispose();
            var listener = new TcpListener(IPAddress.Loopback, tcpPort);
            listener.Start();
            listener.Stop();
            using var rebound = new UdpClient(new IPEndPoint(IPAddress.Loopback, udpPort));
        }

        [Theory]
        [InlineData(ViscaTransportKind.Tcp)]
        [InlineData(ViscaTransportKind.Udp)]
        public async Task GarbageReply_IsNotAHit(ViscaTransportKind kind)
        {
            using var server = new FakeViscaServer(kind) { Responder = _ => new[] { Garbage } };

            var hits = await ViscaProbe.DetectAsync(Host, new[] { (kind, server.Port) }, PerProbe, CancellationToken.None);

            Assert.Empty(hits);
        }

        [Fact]
        public async Task TwoHits_BothReturned_TcpFirst()
        {
            using var udp = new FakeViscaServer(ViscaTransportKind.Udp);
            using var tcp = new FakeViscaServer(ViscaTransportKind.Tcp);
            var candidates = new[] { (ViscaTransportKind.Udp, udp.Port), (ViscaTransportKind.Tcp, tcp.Port) };

            var hits = await ViscaProbe.DetectAsync(Host, candidates, PerProbe, CancellationToken.None);

            Assert.Equal(2, hits.Count());
            Assert.Equal(ViscaTransportKind.Tcp, hits[0].Transport);
            Assert.Equal(tcp.Port, hits[0].Port);
            Assert.Equal(ViscaTransportKind.Udp, hits[1].Transport);
            Assert.Equal(udp.Port, hits[1].Port);
        }
    }
}
