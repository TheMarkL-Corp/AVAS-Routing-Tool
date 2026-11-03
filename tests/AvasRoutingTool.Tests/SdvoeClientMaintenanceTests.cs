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

        // ---- A6: chip reboot (Telnet "reboot <mac>", REST fallback {op: reboot}) ----

        private static AvasDevice Avas(int chip = 0, int vendor = 105, bool rx = false) =>
            new() { DeviceId = "74fe488b07bb", DeviceName = "TX2", IsTransmitter = !rx, IsReceiver = rx, ChipId = chip, VendorId = vendor };

        [Fact]
        public async Task RebootChip_OverTelnet_SendsRebootAndTheMac_AndNothingOverRest()
        {
            using var telnet = new FakeTelnetServer();
            var h = new RecordingHandler();
            var c = Client(h);
            Assert.True(await c.ConnectAsync("127.0.0.1", telnet.Port, 8080));

            Assert.True(await c.RebootChipAsync(Avas(), "74:FE:48:8B:07:BC"));

            Assert.Contains("reboot 74fe488b07bc", telnet.Lines);
            Assert.Empty(h.Calls);
        }

        [Fact]
        public async Task RebootChip_WithoutTelnet_FallsBackToRest()
        {
            var h = new RecordingHandler();

            Assert.True(await Client(h).RebootChipAsync(Avas(), "74fe488b07bb"));

            var call = Assert.Single(h.Calls);
            Assert.Equal("/api/device/74fe488b07bb", call.Path);
            Assert.Equal("reboot", call.Body.GetProperty("op").GetString());
        }

        [Fact]
        public async Task RebootChip_WhenTelnetRefuses_FallsBackToRest()
        {
            using var telnet = new FakeTelnetServer { Status = _ => "ERROR" };
            var h = new RecordingHandler();
            var c = Client(h);
            await c.ConnectAsync("127.0.0.1", telnet.Port, 8080);

            Assert.True(await c.RebootChipAsync(Avas(), "74fe488b07bb"));

            Assert.Single(h.Calls);
        }

        [Fact]
        public async Task RebootChip_WhenEverythingRefuses_ReturnsFalse()
        {
            var h = new RecordingHandler { FailFromCall = 1 };
            Assert.False(await Client(h).RebootChipAsync(Avas(), "74fe488b07bb"));
        }

        [Fact]
        public async Task RebootChip_IsRefusedForNonAvas_WithoutAnyCall()
        {
            using var telnet = new FakeTelnetServer();
            var h = new RecordingHandler();
            var c = Client(h);
            await c.ConnectAsync("127.0.0.1", telnet.Port, 8080);

            Assert.False(await c.RebootChipAsync(Avas(vendor: 7), "74fe488b07bb"));

            Assert.Empty(h.Calls);
            Assert.DoesNotContain(telnet.Lines, l => l.StartsWith("reboot"));
        }
    }
}
