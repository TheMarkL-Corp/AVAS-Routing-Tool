using System;
using System.Buffers.Binary;
using System.Threading;
using AvasRoutingTool.Rtp;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class UnifiedRtpDemuxReceiverTests
    {
        [Fact]
        public void UnifiedRtpDemuxReceiver_CanStartAndStopListening()
        {
            using var receiver = new UnifiedRtpDemuxReceiver();
            Assert.False(receiver.IsListening);

            receiver.StartListening(new[] { 15004, 15000 });
            Assert.True(receiver.IsListening);

            receiver.StopListening();
            Assert.False(receiver.IsListening);
        }

        [Fact]
        public void UnifiedRtpDemuxReceiver_RegisterAndUnregisterChannel_DoesNotThrow()
        {
            using var receiver = new UnifiedRtpDemuxReceiver();
            receiver.StartListening(new[] { 15006 });

            bool packetReceived = false;
            receiver.RegisterChannel("225.0.0.1", (data, offset, length) =>
            {
                packetReceived = true;
            });

            receiver.UnregisterChannel("225.0.0.1");
            receiver.StopListening();

            Assert.False(packetReceived);
        }

        [Fact]
        public void TryResolveChannel_UnregisteredSenders_GoToTheChannelOfTheirMulticastGroup()
        {
            // Live finding: the preview senders (10.32.32.22 / .14) are not the device IPs the channels are keyed by.
            // The /24 fallback used to hand BOTH senders to whichever single channel was listening, and cache that, so one
            // card received two interleaved streams (inflated fps). The multicast group a packet was sent to says whose it is.
            using var receiver = new UnifiedRtpDemuxReceiver();
            var tx1 = receiver.GetOrCreateChannel("10.32.32.21", "225.0.0.1");
            var tx2 = receiver.GetOrCreateChannel("10.32.32.12", "225.0.0.2");
            tx1.StartListening("225.0.0.1", 15010); // tx2 is mid-refresh: stopped, so only one channel "listens"

            Assert.True(receiver.TryResolveChannel("10.32.32.22", "225.0.0.1", out var forTx1));
            Assert.Same(tx1, forTx1);
            Assert.True(receiver.TryResolveChannel("10.32.32.14", "225.0.0.2", out var forTx2));
            Assert.Same(tx2, forTx2);

            // and nothing was cached against the wrong card
            Assert.True(receiver.TryResolveChannel("10.32.32.14", "225.0.0.2", out var again));
            Assert.Same(tx2, again);
            receiver.StopListening();
        }

        [Fact]
        public void UnifiedRtpDemuxReceiver_AliasRouting_ResolvesCorrectChannel()
        {
            using var receiver = new UnifiedRtpDemuxReceiver();
            var primaryChannel = receiver.GetOrCreateChannel("10.32.32.12", "225.0.0.1");
            receiver.RegisterAlias("10.32.32.14", "10.32.32.12");

            bool resolvedDirect = receiver.TryResolveChannel("10.32.32.12", out var ch1);
            Assert.True(resolvedDirect);
            Assert.Same(primaryChannel, ch1);

            bool resolvedAlias = receiver.TryResolveChannel("10.32.32.14", out var ch2);
            Assert.True(resolvedAlias);
            Assert.Same(primaryChannel, ch2);

            bool resolvedUnknown = receiver.TryResolveChannel("192.168.1.99", out var ch3);
            Assert.False(resolvedUnknown);
            Assert.Null(ch3);
        }
    }
}
