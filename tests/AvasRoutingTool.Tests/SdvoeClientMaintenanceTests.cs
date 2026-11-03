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

        private const string Chip0Mac = "74:FE:48:8B:07:BB";
        private const string Chip1Mac = "74:FE:48:8B:07:BC";

        private static AvasDevice Tx(int chip = 0, int vendor = 105, bool active = true) =>
            new()
            {
                DeviceId = chip == 0 ? Chip0Mac : Chip1Mac, DeviceName = chip == 0 ? "TX2_SFPA" : "TX2_SFPB",
                IsTransmitter = true, ChipId = chip, VendorId = vendor, IsActive = active
            };

        private static string OpOf(RecordingHandler h, int i) => h.Calls[i].Body.GetProperty("op").GetString()!;

        [Theory]
        [InlineData(LinkMode.Dual, "DUAL")]
        [InlineData(LinkMode.Single, "SINGLE")]
        public async Task SetsTheSameMode_OnChip0AndChip1_ThenRebootsBothChips(LinkMode target, string wire)
        {
            var h = new RecordingHandler();

            Assert.True(await Client(h).SetLinkModeAsync(Tx(0), Tx(1), target));

            Assert.Equal(4, h.Calls.Count);
            Assert.Equal("/api/device/74fe488b07bc", h.Calls[3].Path);
            Assert.Equal("reboot", OpOf(h, 3));
            Assert.Equal("/api/device/74fe488b07bb", h.Calls[0].Path);
            Assert.Equal("set:multi_link", OpOf(h, 0));
            Assert.Equal(wire, h.Calls[0].Body.GetProperty("mode").GetString());
            Assert.Equal("/api/device/74fe488b07bc", h.Calls[1].Path);
            Assert.Equal("set:multi_link", OpOf(h, 1));
            Assert.Equal(wire, h.Calls[1].Body.GetProperty("mode").GetString());
            Assert.Equal("/api/device/74fe488b07bb", h.Calls[2].Path);
            Assert.Equal("reboot", OpOf(h, 2));
        }

        [Fact]
        public async Task WhenChip0RefusesTheMode_NothingElseIsSent()
        {
            var h = new RecordingHandler { FailFromCall = 1 };

            Assert.False(await Client(h).SetLinkModeAsync(Tx(0), Tx(1), LinkMode.Dual));

            Assert.Single(h.Calls);
        }

        [Fact]
        public async Task WhenChip1RefusesTheMode_Chip0IsPutBack_NoRebootIsSent_AndItIsAFailure()
        {
            var h = new RecordingHandler
            {
                Responder = (m, p, b) => p.EndsWith("74fe488b07bc")
                    ? "{\"status\":\"ERROR\",\"request_id\":null,\"result\":null,\"error\":null}"
                    : null
            };

            Assert.False(await Client(h).SetLinkModeAsync(Tx(0), Tx(1), LinkMode.Dual));

            Assert.Equal(3, h.Calls.Count);
            Assert.Equal("/api/device/74fe488b07bb", h.Calls[2].Path);
            Assert.Equal("set:multi_link", OpOf(h, 2));
            Assert.Equal("SINGLE", h.Calls[2].Body.GetProperty("mode").GetString()); // chip 0 back to the other mode
            Assert.DoesNotContain(h.Calls, c => c.Body.GetProperty("op").GetString() == "reboot");
        }

        [Fact]
        public async Task WhenARebootIsRefused_ItIsAFailure_AndTheOtherChipIsStillRebooted()
        {
            // chip 0's reboot refused: chip 1 is still rebooted so the pair restarts together
            var h = new RecordingHandler { Responder = (m, p, b) => b.GetProperty("op").GetString() == "reboot" && p.EndsWith("07bb") ? "{\"status\":\"ERROR\",\"request_id\":null,\"result\":null,\"error\":null}" : null };
            Assert.False(await Client(h).SetLinkModeAsync(Tx(0), Tx(1), LinkMode.Dual));
            Assert.Equal(4, h.Calls.Count);

            // chip 1's reboot refused
            var h2 = new RecordingHandler { Responder = (m, p, b) => b.GetProperty("op").GetString() == "reboot" && p.EndsWith("07bc") ? "{\"status\":\"ERROR\",\"request_id\":null,\"result\":null,\"error\":null}" : null };
            Assert.False(await Client(h2).SetLinkModeAsync(Tx(0), Tx(1), LinkMode.Dual));
        }

        [Fact]
        public async Task ItIsRefusedWithoutAnyCall_UnlessBothChipsAreOnlineAvasChipsOfTheRightNumbers()
        {
            var h = new RecordingHandler();
            var c = Client(h);

            Assert.False(await c.SetLinkModeAsync(Tx(0), null, LinkMode.Dual));                      // no chip 1
            Assert.False(await c.SetLinkModeAsync(Tx(0), Tx(1, active: false), LinkMode.Dual));      // chip 1 offline
            Assert.False(await c.SetLinkModeAsync(Tx(0, active: false), Tx(1), LinkMode.Dual));      // chip 0 offline
            Assert.False(await c.SetLinkModeAsync(Tx(0, vendor: 7), Tx(1), LinkMode.Dual));          // not AVAS
            Assert.False(await c.SetLinkModeAsync(Tx(0), Tx(1, vendor: 7), LinkMode.Dual));
            Assert.False(await c.SetLinkModeAsync(Tx(1), Tx(0), LinkMode.Dual));                     // swapped chips
            Assert.False(await c.SetLinkModeAsync(Tx(0), Tx(0), LinkMode.Dual));                     // the same chip twice
            Assert.False(await c.SetLinkModeAsync(Tx(0), Tx(1), LinkMode.Unknown));
            Assert.False(await c.SetLinkModeAsync(new AvasDevice { DeviceId = "r", IsReceiver = true }, Tx(1), LinkMode.Dual));

            Assert.Empty(h.Calls);
        }

        // ---- A7: MCU reboot (rs232 port 1 of chip 0: switch:api, then send:rs232 "set system reboot") ----

        [Fact]
        public async Task RebootMcu_PutsTheUartInApiMode_ThenSendsTheRebootToChip0()
        {
            var h = new RecordingHandler();

            Assert.True(await Client(h).RebootMcuAsync(Avas()));

            Assert.Equal(2, h.Calls.Count);
            Assert.All(h.Calls, c => Assert.Equal("/api/device/74fe488b07bb", c.Path));
            Assert.Equal("switch:api", h.Calls[0].Body.GetProperty("op").GetString());
            Assert.Equal("rs232", h.Calls[0].Body.GetProperty("stream_type").GetString());
            Assert.Equal(1, h.Calls[0].Body.GetProperty("stream_index").GetInt32());
            Assert.Equal("send:rs232", h.Calls[1].Body.GetProperty("op").GetString());
            Assert.Equal(1, h.Calls[1].Body.GetProperty("port_index").GetInt32());
            Assert.Equal("set system reboot\r\n", h.Calls[1].Body.GetProperty("data_string").GetString());
        }

        [Fact]
        public async Task RebootMcu_WhenRestRefuses_FallsBackToTheTelnetSendCommand()
        {
            using var telnet = new FakeTelnetServer();
            var h = new RecordingHandler { FailFromCall = 1 };
            var c = Client(h);
            await c.ConnectAsync("127.0.0.1", telnet.Port, 8080);

            Assert.True(await c.RebootMcuAsync(Avas()));

            Assert.Contains("send 74fe488b07bb rs232:1 set system reboot", telnet.Lines);
        }

        [Fact]
        public async Task RebootMcu_WhenEverythingRefuses_ReturnsFalse()
        {
            var h = new RecordingHandler { FailFromCall = 1 };
            Assert.False(await Client(h).RebootMcuAsync(Avas()));
        }

        [Fact]
        public async Task RebootMcu_IsRefusedForNonAvas_AndForTheCompanionChip_WithoutAnyCall()
        {
            var h = new RecordingHandler();
            var c = Client(h);

            Assert.False(await c.RebootMcuAsync(Avas(vendor: 7)));
            Assert.False(await c.RebootMcuAsync(Avas(chip: 1)));

            Assert.Empty(h.Calls);
        }

        // ---- A8: MCU factory reset (rs232 port 1 of chip 0: send:rs232 "set factory default") ----

        [Fact]
        public async Task FactoryResetMcu_SendsSetFactoryDefaultToChip0()
        {
            var h = new RecordingHandler();

            Assert.True(await Client(h).FactoryResetMcuAsync(Avas()));

            Assert.Equal(2, h.Calls.Count);
            Assert.All(h.Calls, c => Assert.Equal("/api/device/74fe488b07bb", c.Path));
            Assert.Equal("switch:api", h.Calls[0].Body.GetProperty("op").GetString());
            Assert.Equal("send:rs232", h.Calls[1].Body.GetProperty("op").GetString());
            Assert.Equal("set factory default\r\n", h.Calls[1].Body.GetProperty("data_string").GetString());
        }

        [Fact]
        public async Task FactoryResetMcu_WhenRestRefuses_FallsBackToTheTelnetSendCommand()
        {
            using var telnet = new FakeTelnetServer();
            var h = new RecordingHandler { FailFromCall = 1 };
            var c = Client(h);
            await c.ConnectAsync("127.0.0.1", telnet.Port, 8080);

            Assert.True(await c.FactoryResetMcuAsync(Avas()));

            Assert.Contains("send 74fe488b07bb rs232:1 set factory default", telnet.Lines);
        }

        [Fact]
        public async Task FactoryResetMcu_IsRefusedForNonAvas_AndForTheCompanionChip_WithoutAnyCall()
        {
            var h = new RecordingHandler();
            var c = Client(h);

            Assert.False(await c.FactoryResetMcuAsync(Avas(vendor: 7)));
            Assert.False(await c.FactoryResetMcuAsync(Avas(chip: 1)));

            Assert.Empty(h.Calls);
        }

        // ---- A9: chip factory reset (Telnet "factory <mac>", REST {op: factory_default}, then {op: factory}) ----

        [Fact]
        public async Task FactoryResetChip_OverTelnet_SendsFactoryAndTheMac_AndNothingOverRest()
        {
            using var telnet = new FakeTelnetServer();
            var h = new RecordingHandler();
            var c = Client(h);
            await c.ConnectAsync("127.0.0.1", telnet.Port, 8080);

            Assert.True(await c.FactoryResetChipAsync(Avas(), "74:FE:48:8B:07:BC"));

            Assert.Contains("factory 74fe488b07bc", telnet.Lines);
            Assert.Empty(h.Calls);
        }

        [Fact]
        public async Task FactoryResetChip_WithoutTelnet_UsesRestFactoryDefault()
        {
            var h = new RecordingHandler();

            Assert.True(await Client(h).FactoryResetChipAsync(Avas(), "74fe488b07bb"));

            var call = Assert.Single(h.Calls);
            Assert.Equal("/api/device/74fe488b07bb", call.Path);
            Assert.Equal("factory_default", call.Body.GetProperty("op").GetString());
        }

        [Fact]
        public async Task FactoryResetChip_WhenFactoryDefaultIsRefused_TriesTheAlternativeFactoryOp()
        {
            var h = new RecordingHandler
            {
                Responder = (m, p, b) => b.GetProperty("op").GetString() == "factory_default"
                    ? "{\"status\":\"ERROR\",\"request_id\":null,\"result\":null,\"error\":null}"
                    : null
            };

            Assert.True(await Client(h).FactoryResetChipAsync(Avas(), "74fe488b07bb"));

            Assert.Equal(new[] { "factory_default", "factory" }, h.Calls.Select(c => c.Body.GetProperty("op").GetString()).ToArray());
        }

        [Fact]
        public async Task FactoryResetChip_WhenEverythingRefuses_ReturnsFalse()
        {
            var h = new RecordingHandler { FailFromCall = 1 };
            Assert.False(await Client(h).FactoryResetChipAsync(Avas(), "74fe488b07bb"));
        }

        [Fact]
        public async Task FactoryResetChip_IsRefusedForNonAvas_WithoutAnyCall()
        {
            using var telnet = new FakeTelnetServer();
            var h = new RecordingHandler();
            var c = Client(h);
            await c.ConnectAsync("127.0.0.1", telnet.Port, 8080);

            Assert.False(await c.FactoryResetChipAsync(Avas(vendor: 7), "74fe488b07bb"));

            Assert.Empty(h.Calls);
            Assert.DoesNotContain(telnet.Lines, l => l.StartsWith("factory"));
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
