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
