using System.Collections.Generic;
using AvasRoutingTool.Sdvoe;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class AvasDeviceTests
    {
        [Fact]
        public void AvasDevice_WhenVendorIdIs105AndIsTransmitter_PreviewIsSupported()
        {
            var device = new AvasDevice
            {
                DeviceId = "74fe488b07bb",
                DeviceName = "TX2_DUAL LINK_SFPA",
                VendorId = 105,
                IsTransmitter = true
            };

            Assert.True(device.IsAvasDevice);
            Assert.True(device.IsPreviewSupported);
        }

        [Fact]
        public void AvasDevice_WhenVendorIdIsNot105_PreviewIsNotSupported()
        {
            var device = new AvasDevice
            {
                DeviceId = "0016c0589c1e",
                DeviceName = "VEGA_1100",
                VendorId = 192,
                IsTransmitter = true
            };

            Assert.False(device.IsAvasDevice);
            Assert.False(device.IsPreviewSupported);
        }

        [Fact]
        public void AvasDevice_WhenReceiver_PreviewIsNotSupported()
        {
            var device = new AvasDevice
            {
                DeviceId = "74fe483c059e",
                DeviceName = "RX1_c059e_SFPA",
                VendorId = 105,
                IsTransmitter = false,
                IsReceiver = true
            };

            Assert.True(device.IsAvasDevice);
            Assert.False(device.IsPreviewSupported);
        }

        [Fact]
        public void AvasDevice_SupportsLinkModeAndNodeIps()
        {
            var device = new AvasDevice
            {
                LinkMode = "Dual Link"
            };

            device.NodeIps.Add("10.32.32.12");
            device.NodeIps.Add("10.32.32.14");

            Assert.Equal("Dual Link", device.LinkMode);
            Assert.Equal(2, device.NodeIps.Count);
        }
    }
}
