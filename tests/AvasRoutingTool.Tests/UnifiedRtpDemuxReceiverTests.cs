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

            // Use non-privileged ephemeral test ports to avoid collisions
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
    }
}
