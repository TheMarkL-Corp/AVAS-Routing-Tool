using System.Collections.Generic;
using System.Linq;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class AdvancedViewModelTests
    {
        private static AvasDevice Tx(string mac, string name, int vendor = 105) =>
            new() { DeviceId = mac, DeviceName = name, IsTransmitter = true, VendorId = vendor };

        private static AvasDevice Rx(string mac, string name) =>
            new() { DeviceId = mac, DeviceName = name, IsReceiver = true };

        [Fact]
        public void OneRowPerDiscoveredDevice_WithRoleAndAvasFlag()
        {
            var vm = new AdvancedViewModel();
            vm.SetDevices(new List<AvasDevice> { Tx("74fe488b07bb", "TX2"), Rx("74fe483c059e", "RX1"), Tx("0016c0589c1e", "VEGA", vendor: 7) });

            Assert.Equal(3, vm.Rows.Count);
            var tx2 = vm.Rows.Single(r => r.Name == "TX2");
            Assert.Equal("74fe488b07bb", tx2.Mac);
            Assert.Equal("TX", tx2.Role);
            Assert.True(tx2.IsAvas);
            Assert.Equal("RX", vm.Rows.Single(r => r.Name == "RX1").Role);
            Assert.False(vm.Rows.Single(r => r.Name == "VEGA").IsAvas);
        }

        [Fact]
        public void RowsFollowTheDiscoveryRefresh()
        {
            var vm = new AdvancedViewModel();
            vm.SetDevices(new List<AvasDevice> { Tx("a", "TX1"), Rx("b", "RX1") });
            vm.SetDevices(new List<AvasDevice> { Tx("a", "TX1") });

            Assert.Single(vm.Rows);
            Assert.Equal("TX1", vm.Rows[0].Name);
        }

        [Fact]
        public void TransmittersListFirst_ThenReceivers_ByName()
        {
            var vm = new AdvancedViewModel();
            vm.SetDevices(new List<AvasDevice> { Rx("r", "A-RX"), Tx("t2", "B-TX"), Tx("t1", "A-TX") });

            Assert.Equal(new[] { "A-TX", "B-TX", "A-RX" }, vm.Rows.Select(r => r.Name).ToArray());
        }
    }
}
