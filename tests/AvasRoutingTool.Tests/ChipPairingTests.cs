using System.Collections.Generic;
using System.Linq;
using AvasRoutingTool.Sdvoe;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>A dual-link TX is two discovered chips (chip 0 and chip 1); they are shown and switched as one device.</summary>
    public class ChipPairingTests
    {
        private static AvasDevice Chip(string mac, string name, int chip, bool tx = true, int vendor = 105, bool active = true) =>
            new() { DeviceId = mac, DeviceName = name, ChipId = chip, IsTransmitter = tx, IsReceiver = !tx, VendorId = vendor, IsActive = active };

        [Fact]
        public void ConsecutiveMacs_ArePaired_AndTheCompanionIsNotListedOnItsOwn()
        {
            var devices = new[]
            {
                Chip("74fe488b07bb", "TX2_A", 0), Chip("74fe488b07bc", "other", 1),
                Chip("74fe483c059e", "RX1", 0, tx: false)
            };

            var pairs = ChipPairing.Build(devices);

            Assert.Equal(2, pairs.Count);
            Assert.Equal("74fe488b07bc", pairs[0].Companion!.DeviceId);
            Assert.Null(pairs[1].Companion);
            Assert.Equal("RX1", pairs[1].Primary.DeviceName);
        }

        [Fact]
        public void NamesThatDifferOnlyBySfpAAndB_ArePaired_EvenWithFarApartMacs()
        {
            var pairs = ChipPairing.Build(new[] { Chip("74fe488b0001", "TX2_DUAL LINK_SFPA", 0), Chip("74fe488b0999", "TX2_DUAL LINK_SFPB", 1) });

            Assert.Equal("74fe488b0999", Assert.Single(pairs).Companion!.DeviceId);
        }

        [Fact]
        public void ADeclaredCompanion_WinsOverTheHeuristics()
        {
            var devices = new[] { Chip("74fe488b0001", "TX1", 0), Chip("74fe488b0002", "TX1 next", 1), Chip("74fe488b0777", "declared", 1) };
            var declared = new Dictionary<string, string> { ["74fe488b0001"] = "74fe488b0777" };

            var pairs = ChipPairing.Build(devices, declared);

            Assert.Equal("74fe488b0777", pairs[0].Companion!.DeviceId);
            Assert.Equal("74fe488b0002", pairs[1].Primary.DeviceId); // the other chip 1 stays a device of its own
        }

        [Fact]
        public void AChip1_IsUsedByOnePrimaryOnly()
        {
            var devices = new[] { Chip("74fe488b0001", "TX_SFPA", 0), Chip("74fe488b0002", "TX_SFPA", 0), Chip("74fe488b0003", "TX_SFPB", 1) };

            var pairs = ChipPairing.Build(devices);

            Assert.Single(pairs, p => p.Companion != null);
            Assert.Equal(2, pairs.Count);
        }

        [Fact]
        public void AChip0WithoutAChip1_HasNoCompanion_AndAnOrphanChip1_IsListedAlone()
        {
            var pairs = ChipPairing.Build(new[] { Chip("74fe488b0001", "Solo", 0), Chip("74fe488b0900", "Orphan_SFPB", 1) });

            Assert.Equal(2, pairs.Count);
            Assert.All(pairs, p => Assert.Null(p.Companion));
        }

        [Fact]
        public void NonAvasAndReceivers_AreNeverPaired()
        {
            var pairs = ChipPairing.Build(new[]
            {
                Chip("0016c0589c1e", "VEGA", 0, vendor: 7), Chip("0016c0589c1f", "VEGA", 1, vendor: 7),
                Chip("74fe483c059e", "RX", 0, tx: false), Chip("74fe483c059f", "RX", 1, tx: false)
            });

            Assert.Equal(4, pairs.Count);
            Assert.All(pairs, p => Assert.Null(p.Companion));
        }

        [Fact]
        public void TheOfflineChip1_IsStillPaired_SoTheRowCanSayItIsOffline()
        {
            var pairs = ChipPairing.Build(new[] { Chip("74fe488b0001", "TX_SFPA", 0), Chip("74fe488b0002", "TX_SFPB", 1, active: false) });

            Assert.False(Assert.Single(pairs).Companion!.IsActive);
        }
    }
}
