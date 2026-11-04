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

namespace AvasRoutingTool.Tests
{
    public class AdvancedViewModelStatusTests
    {
        private sealed class FakeReader : AvasRoutingTool.Sdvoe.IDeviceReader
        {
            public List<string> Reads { get; } = new();
            public System.Threading.Tasks.Task<string?> ReadDeviceSubsetAsync(string mac, string subset)
            {
                Reads.Add(mac);
                string json = "{\"status\":\"SUCCESS\",\"request_id\":null,\"error\":null,\"result\":{\"devices\":[{\"status\":{\"temperature\":64},\"nodes\":[{\"type\":\"MULTI_LINK_TRANSMITTER\",\"configuration\":{\"link_mode\":\"DUAL\"},\"status\":{\"companions\":[{\"device_id\":\"c1\"}]}}]}]}}";
                return System.Threading.Tasks.Task.FromResult<string?>(json);
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task RefreshStatus_FillsAvasRows_AndSkipsNonAvas()
        {
            var vm = new AdvancedViewModel();
            vm.SetDevices(new List<AvasDevice>
            {
                new() { DeviceId = "t1", DeviceName = "TX1", IsTransmitter = true },
                new() { DeviceId = "r1", DeviceName = "RX1", IsReceiver = true },
                new() { DeviceId = "v1", DeviceName = "VEGA", IsTransmitter = true, VendorId = 7 }
            });
            var reader = new FakeReader();

            await vm.RefreshStatusAsync(reader);

            var tx = vm.Rows.Single(r => r.Name == "TX1");
            Assert.Equal("64 °C", tx.TemperatureText);
            Assert.Equal("Warm", tx.TemperatureState);
            Assert.Equal("Dual link", tx.LinkMode);
            var rx = vm.Rows.Single(r => r.Name == "RX1");
            Assert.Equal("64 °C", rx.TemperatureText);
            Assert.Equal("n/a", rx.LinkMode);
            Assert.Equal("n/a", vm.Rows.Single(r => r.Name == "VEGA").TemperatureText);
            Assert.DoesNotContain("v1", reader.Reads);
            Assert.Equal(1, reader.Reads.Count(m => m == "r1"));
        }
    }
}
