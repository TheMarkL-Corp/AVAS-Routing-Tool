using System;
using System.Linq;
using System.Threading.Tasks;
using AvasRoutingTool.Sdvoe;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>A5: SetLinkModeAsync = set:multi_link to the primary chip, then the primary-chip reboot (wrapper MultiLinkService).</summary>
    public class SdvoeClientMaintenanceTests
    {
        private static SdvoeClient Client(RecordingHandler h) =>
            new(h) { PollInterval = TimeSpan.FromMilliseconds(5), RequestTimeout = TimeSpan.FromMilliseconds(400), LinkSettleDelay = TimeSpan.Zero };

        private static AvasDevice Tx(int chip = 0, int vendor = 105) =>
            new() { DeviceId = "74:FE:48:8B:07:BB", DeviceName = "TX2", IsTransmitter = true, ChipId = chip, VendorId = vendor };

        [Theory]
        [InlineData(LinkMode.Dual, "DUAL")]
        [InlineData(LinkMode.Single, "SINGLE")]
        public async Task SetsTheMode_ThenRebootsThePrimaryChip(LinkMode target, string wire)
        {
            var h = new RecordingHandler();

            Assert.True(await Client(h).SetLinkModeAsync(Tx(), target));

            Assert.Equal(2, h.Calls.Count);
            Assert.All(h.Calls, c => Assert.Equal("/api/device/74fe488b07bb", c.Path));
            Assert.Equal("set:multi_link", h.Calls[0].Body.GetProperty("op").GetString());
            Assert.Equal(wire, h.Calls[0].Body.GetProperty("mode").GetString());
            Assert.Equal("reboot", h.Calls[1].Body.GetProperty("op").GetString());
        }

        [Fact]
        public async Task WhenTheModeIsRefused_NoRebootIsSent()
        {
            var h = new RecordingHandler { FailFromCall = 1 };

            Assert.False(await Client(h).SetLinkModeAsync(Tx(), LinkMode.Dual));

            Assert.Single(h.Calls);
        }

        [Fact]
        public async Task WhenTheRebootIsRefused_ItIsReportedAsAFailure()
        {
            var h = new RecordingHandler { FailFromCall = 2 };
            Assert.False(await Client(h).SetLinkModeAsync(Tx(), LinkMode.Dual));
        }

        [Fact]
        public async Task NonAvasDevice_CompanionChip_AndUnknownTarget_AreRefusedWithoutAnyCall()
        {
            var h = new RecordingHandler();
            var c = Client(h);

            Assert.False(await c.SetLinkModeAsync(Tx(vendor: 7), LinkMode.Dual));
            Assert.False(await c.SetLinkModeAsync(Tx(chip: 1), LinkMode.Dual));
            Assert.False(await c.SetLinkModeAsync(Tx(), LinkMode.Unknown));
            Assert.False(await c.SetLinkModeAsync(new AvasDevice { DeviceId = "r", IsReceiver = true }, LinkMode.Dual));

            Assert.Empty(h.Calls);
        }
    }
}
