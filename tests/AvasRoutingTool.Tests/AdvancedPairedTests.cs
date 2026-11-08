using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// The two chips of a dual-link TX are one device on the Advanced page: one row, one status, one switch. The link switch needs
    /// both chips online and companions, and sets the same mode on both.
    /// </summary>
    public class AdvancedPairedTests
    {
        private sealed class FakeSwitcher : ILinkSwitcher
        {
            public List<(string Chip0, string? Chip1, LinkMode Target)> Calls { get; } = new();
            public bool Result { get; set; } = true;

            public Task<bool> SetLinkModeAsync(AvasDevice chip0, AvasDevice? chip1, LinkMode target)
            {
                Calls.Add((chip0.DeviceId, chip1?.DeviceId, target));
                return Task.FromResult(Result);
            }
        }

        /// <summary>Answers a <c>get device</c> read per MAC: active flag and the companion the chip reports; a missing entry is an unreachable chip.</summary>
        private sealed class FakeReader : IDeviceReader
        {
            public Dictionary<string, string> Replies { get; } = new();

            public Task<string?> ReadDeviceSubsetAsync(string mac, string subset) =>
                Task.FromResult(Replies.TryGetValue(SdvoeClient.NormalizeMac(mac), out var r) ? r : null);

            public static string Reply(bool active, string mode, string companion = "") =>
                "{\"status\":\"SUCCESS\",\"request_id\":null,\"error\":null,\"result\":{\"devices\":[{\"status\":{\"active\":" + (active ? "true" : "false") + ",\"temperature\":60}," +
                "\"nodes\":[{\"type\":\"MULTI_LINK_TRANSMITTER\",\"configuration\":{\"link_mode\":\"" + mode + "\"},\"status\":{\"companions\":" +
                (companion == "" ? "[]" : "[{\"device_id\":\"" + companion + "\"}]") + ",\"link_capabilities\":[{\"mode\":\"SINGLE\"},{\"mode\":\"DUAL\"}]}}]}]}}";
        }

        private sealed class Rig
        {
            public AdvancedViewModel Vm { get; } = new();
            public FakeSwitcher Switcher { get; } = new();
            public FakeReader Reader { get; } = new();
            public List<string> Order { get; } = new();
            public List<IReadOnlyList<string>> Impacts { get; } = new();
            public List<IReadOnlyList<string>> Unrouted { get; } = new();
            public DateTime Now { get; set; } = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
            public bool Answer { get; set; } = true;

            public AvasDevice Chip0 { get; } = new() { DeviceId = "74fe488b07bb", DeviceName = "TX2_SFPA", IsTransmitter = true, ChipId = 0 };
            public AvasDevice Chip1 { get; } = new() { DeviceId = "74fe488b07bc", DeviceName = "TX2_SFPB", IsTransmitter = true, ChipId = 1 };
            public AvasDevice Rx { get; } = new() { DeviceId = "74fe483c059e", DeviceName = "RX1", IsReceiver = true };

            public Rig(bool useReader = false)
            {
                Vm.Switcher = Switcher;
                Vm.Clock = () => Now;
                Vm.ImpactProvider = macs => { Impacts.Add(macs); return LinkSwitchImpact.None; };
                Vm.UnrouteFromMultiViews = macs => { Order.Add("unroute"); Unrouted.Add(macs); return Task.CompletedTask; };
                Vm.Confirm = _ => { Order.Add("confirm"); return Task.FromResult(Answer); };
                if (useReader) Vm.Reader = Reader;
                Reader.Replies["74fe488b07bb"] = FakeReader.Reply(true, "SINGLE");
                Reader.Replies["74fe488b07bc"] = FakeReader.Reply(true, "SINGLE");
            }

            public AdvancedDeviceRowViewModel Show(LinkMode mode = LinkMode.Single, string caps = "SINGLE,DUAL", params AvasDevice[] devices)
            {
                Vm.SetDevices(devices.Length > 0 ? devices : new[] { Chip0, Chip1, Rx });
                var row = Vm.Rows.First(r => r.Mac == Chip0.DeviceId);
                row.ApplyLinkMode(new LinkModeInfo(mode, mode == LinkMode.Dual ? Chip1.DeviceId : "NONE", caps));
                return row;
            }
        }

        // ---- one entity, with the state of both chips ----

        [Fact]
        public void TheTwoChips_AreOneRow_WithBothChipsOnline()
        {
            var r = new Rig();

            r.Show();

            Assert.Equal(2, r.Vm.Rows.Count); // the TX and the RX
            var row = r.Vm.Rows[0];
            Assert.Equal("TX2_SFPA", row.Name);
            Assert.True(row.HasCompanion);
            Assert.Equal("74fe488b07bc", row.Companion!.DeviceId);
            Assert.Equal("Chip 0 online · Chip 1 online", row.ChipStatusText);
            Assert.Equal("Online", row.ChipStatusState);
        }

        [Fact]
        public void AnOfflineChip1_IsShown_AndBlocksTheSwitchWithAReason()
        {
            var r = new Rig();
            r.Chip1.IsActive = false;

            var row = r.Show();

            Assert.Equal("Chip 0 online · Chip 1 offline", row.ChipStatusText);
            Assert.Equal("Partly online", row.ChipStatusState);
            Assert.False(row.CanSwitchLink);
            Assert.Contains("chip 1", row.SwitchBlockedReason);
        }

        [Fact]
        public void AnOfflineChip0_IsShown_AndBlocksTheSwitch()
        {
            var r = new Rig();
            r.Chip0.IsActive = false;

            var row = r.Show();

            Assert.Equal("Chip 0 offline · Chip 1 online", row.ChipStatusText);
            Assert.False(row.CanSwitchLink);
            Assert.Contains("chip 0", row.SwitchBlockedReason);
        }

        [Fact]
        public void AMissingChip1_IsShownAsNotFound_AndBlocksTheSwitch()
        {
            var r = new Rig();

            var row = r.Show(devices: new[] { r.Chip0 });

            Assert.False(row.HasCompanion);
            Assert.Equal("Chip 0 online · Chip 1 not found", row.ChipStatusText);
            Assert.Equal("Partly online", row.ChipStatusState);
            Assert.False(row.CanSwitchLink);
            Assert.Contains("chip 1", row.SwitchBlockedReason);
        }

        [Fact]
        public void ASingleLinkOnlyDevice_HasNoSecondChipToMiss()
        {
            var r = new Rig();

            var row = r.Show(LinkMode.Single, caps: "SINGLE", devices: new[] { r.Chip0 });

            Assert.Equal("Online", row.ChipStatusText);
            Assert.Equal("", row.SwitchBlockedReason);
        }

        [Fact]
        public void Temperature_ShowsBothChips_AndTheWorstState()
        {
            var r = new Rig();
            var row = r.Show();

            row.ApplyTemperature(58);
            row.ApplyCompanionTemperature(64);
            Assert.Equal("58 °C / 64 °C", row.TemperatureText);
            Assert.Equal("Warm", row.TemperatureState);

            row.ApplyCompanionTemperature(null);
            Assert.Equal("58 °C / n/a", row.TemperatureText); // a missing reading is never 0
            Assert.Equal("Normal", row.TemperatureState);
        }

        [Fact]
        public async Task RefreshStatus_ReadsTheTemperatureOfBothChips()
        {
            var r = new Rig();
            var row = r.Show();
            r.Reader.Replies["74fe488b07bb"] = FakeReader.Reply(true, "SINGLE");
            r.Reader.Replies["74fe488b07bc"] = FakeReader.Reply(true, "SINGLE").Replace("\"temperature\":60", "\"temperature\":72");

            await r.Vm.RefreshStatusAsync(r.Reader);

            Assert.Equal("60 °C / 72 °C", row.TemperatureText);
            Assert.Equal("Hot", row.TemperatureState);
        }

        [Fact]
        public void TheDangerZone_StillListsEachChip_SoAChipCanBeRebootedAlone()
        {
            var r = new Rig();

            r.Show();

            Assert.Equal(new[] { "TX2_SFPA", "TX2_SFPB", "RX1" }, r.Vm.DangerRows.Select(x => x.Name).ToArray());
        }

        // ---- the switch ----

        [Fact]
        public async Task Cancel_ChangesNothing()
        {
            var r = new Rig { Answer = false };
            var row = r.Show();

            await r.Vm.SwitchLinkAsync(row);

            Assert.Empty(r.Switcher.Calls);
            Assert.Empty(r.Unrouted);
            Assert.False(row.IsRebooting);
        }

        [Fact]
        public async Task Confirm_SetsTheModeOnBothChips_UnroutesBoth_AndBothShowRebooting()
        {
            var r = new Rig();
            var row = r.Show();

            await r.Vm.SwitchLinkAsync(row);

            Assert.Equal(("74fe488b07bb", "74fe488b07bc", LinkMode.Dual), Assert.Single(r.Switcher.Calls));
            Assert.Equal(new[] { "confirm", "unroute" }, r.Order);
            Assert.Equal(new[] { "74fe488b07bb", "74fe488b07bc" }, Assert.Single(r.Unrouted).OrderBy(m => m).ToArray());
            Assert.Equal(new[] { "74fe488b07bb", "74fe488b07bc" }, r.Impacts.Single().OrderBy(m => m).ToArray());
            Assert.True(row.IsRebooting);
            Assert.Equal("Rebooting", row.LinkMode);
        }

        [Fact]
        public async Task FromDual_TheSwitchGoesToSingle_OnBothChips()
        {
            var r = new Rig();
            var row = r.Show(LinkMode.Dual);

            await r.Vm.SwitchLinkAsync(row);

            Assert.Equal(LinkMode.Single, Assert.Single(r.Switcher.Calls).Target);
        }

        [Fact]
        public async Task ARefusedSwitch_UnroutesNothing_ShowsABanner_AndKeepsTheOldMode()
        {
            var r = new Rig();
            r.Switcher.Result = false;
            var row = r.Show();

            await r.Vm.SwitchLinkAsync(row);

            Assert.Empty(r.Unrouted);
            Assert.False(row.IsRebooting);
            Assert.Equal("Single link", row.LinkMode);
            Assert.Contains("TX2_SFPA", r.Vm.Banner);
        }

        [Fact]
        public async Task Rebooting_CoversBothChips_UntilTheyAreRediscovered()
        {
            var r = new Rig();
            var row = r.Show();
            await r.Vm.SwitchLinkAsync(row);

            r.Vm.SetDevices(new[] { r.Chip0, r.Chip1, r.Rx }); // still listed
            Assert.True(r.Vm.Rows[0].IsRebooting);

            r.Vm.SetDevices(new[] { r.Rx });                   // both gone
            r.Vm.SetDevices(new[] { r.Chip0, r.Chip1, r.Rx }); // both back
            Assert.False(r.Vm.Rows[0].IsRebooting);
        }

        [Fact]
        public async Task Rebooting_GivesUpAfterTheTimeout()
        {
            var r = new Rig();
            var row = r.Show();
            await r.Vm.SwitchLinkAsync(row);

            r.Now += AdvancedViewModel.RebootTimeout + TimeSpan.FromSeconds(1);
            r.Vm.SetDevices(new[] { r.Chip0, r.Chip1, r.Rx });

            Assert.False(r.Vm.Rows[0].IsRebooting);
        }

        // ---- checked again at the moment of the switch ----

        [Fact]
        public async Task WhenChip1IsOfflineAtTheMoment_NothingIsAskedOrSent()
        {
            var r = new Rig(useReader: true);
            var row = r.Show();
            r.Reader.Replies["74fe488b07bc"] = FakeReader.Reply(false, "SINGLE");

            await r.Vm.SwitchLinkAsync(row);

            Assert.Empty(r.Order);
            Assert.Empty(r.Switcher.Calls);
            Assert.Contains("chip 1", r.Vm.Banner);
            Assert.Contains("offline", r.Vm.Banner);
        }

        [Fact]
        public async Task WhenChip1CannotBeReached_NothingIsAskedOrSent()
        {
            var r = new Rig(useReader: true);
            var row = r.Show();
            r.Reader.Replies.Remove("74fe488b07bc");

            await r.Vm.SwitchLinkAsync(row);

            Assert.Empty(r.Order);
            Assert.Empty(r.Switcher.Calls);
            Assert.Contains("chip 1", r.Vm.Banner);
        }

        [Fact]
        public async Task WhenChip0ReportsADifferentCompanion_TheyAreNotCompanions_NothingIsSent()
        {
            var r = new Rig(useReader: true);
            var row = r.Show(LinkMode.Dual);
            r.Reader.Replies["74fe488b07bb"] = FakeReader.Reply(true, "DUAL", companion: "74fe488b0999");

            await r.Vm.SwitchLinkAsync(row);

            Assert.Empty(r.Switcher.Calls);
            Assert.Contains("not companions", r.Vm.Banner);
        }

        [Fact]
        public async Task WhenChip1ReportsADifferentCompanion_TheyAreNotCompanions_NothingIsSent()
        {
            var r = new Rig(useReader: true);
            var row = r.Show(LinkMode.Dual);
            r.Reader.Replies["74fe488b07bb"] = FakeReader.Reply(true, "DUAL", companion: "74fe488b07bc");
            r.Reader.Replies["74fe488b07bc"] = FakeReader.Reply(true, "DUAL", companion: "74fe488b0999");

            await r.Vm.SwitchLinkAsync(row);

            Assert.Empty(r.Switcher.Calls);
            Assert.Contains("not companions", r.Vm.Banner);
        }

        [Fact]
        public async Task WhenBothChipsAreOnlineAndCompanions_TheSwitchGoesAhead()
        {
            var r = new Rig(useReader: true);
            var row = r.Show(LinkMode.Dual);
            r.Reader.Replies["74fe488b07bb"] = FakeReader.Reply(true, "DUAL", companion: "74fe488b07bc");
            r.Reader.Replies["74fe488b07bc"] = FakeReader.Reply(true, "DUAL", companion: "74fe488b07bb");

            await r.Vm.SwitchLinkAsync(row);

            Assert.Single(r.Switcher.Calls);
        }
    }
}
