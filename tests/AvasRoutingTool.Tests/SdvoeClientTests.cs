using System.Collections.Generic;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Sdvoe;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class SdvoeClientTests
    {
        private const string RealSemtechDeviceJson = @"{
            ""status"": ""SUCCESS"",
            ""request_id"": null,
            ""result"": {
                ""devices"": [
                    {
                        ""device_id"": ""74fe483c059e"",
                        ""device_name"": ""RX1_c059e_SFPA"",
                        ""identity"": {
                            ""chipset_type"": ""AVP2000"",
                            ""vendor_id"": 105,
                            ""product_id"": 80,
                            ""chip_id"": 0,
                            ""is_receiver"": true,
                            ""is_transmitter"": false
                        },
                        ""status"": {
                            ""active"": true
                        },
                        ""nodes"": [
                            {
                                ""type"": ""NETWORK_INTERFACE"",
                                ""status"": {
                                    ""mac_address"": ""74fe483c059e"",
                                    ""ip"": {
                                        ""address"": ""10.32.32.17""
                                    }
                                }
                            }
                        ]
                    },
                    {
                        ""device_id"": ""74fe488b07bb"",
                        ""device_name"": ""TX2_DUAL LINK_SFPA"",
                        ""identity"": {
                            ""chipset_type"": ""AVP2000"",
                            ""vendor_id"": 105,
                            ""product_id"": 81,
                            ""chip_id"": 0,
                            ""is_receiver"": false,
                            ""is_transmitter"": true
                        },
                        ""status"": {
                            ""active"": true
                        },
                        ""nodes"": [
                            {
                                ""type"": ""NETWORK_INTERFACE"",
                                ""status"": {
                                    ""mac_address"": ""74fe488b07bb"",
                                    ""ip"": {
                                        ""address"": ""10.32.32.12""
                                    }
                                }
                            }
                        ]
                    }
                ]
            }
        }";

        [Fact]
        public void ParseDevicesJson_ParsesSemtechRealDevicesCorrectly()
        {
            var devices = SdvoeClient.ParseDevicesJson(RealSemtechDeviceJson);

            Assert.Equal(2, devices.Count);

            var rx = devices[0];
            Assert.Equal("74fe483c059e", rx.DeviceId);
            Assert.Equal("RX1_c059e_SFPA", rx.DeviceName);
            Assert.Equal("10.32.32.17", rx.IpAddress);
            Assert.True(rx.IsReceiver);
            Assert.False(rx.IsTransmitter);
            Assert.True(rx.IsActive);
            Assert.Equal(105, rx.VendorId);
            Assert.Equal(80, rx.ProductId);

            var tx = devices[1];
            Assert.Equal("74fe488b07bb", tx.DeviceId);
            Assert.Equal("TX2_DUAL LINK_SFPA", tx.DeviceName);
            Assert.Equal("10.32.32.12", tx.IpAddress);
            Assert.False(tx.IsReceiver);
            Assert.True(tx.IsTransmitter);
            Assert.True(tx.IsActive);
            Assert.Equal(105, tx.VendorId);
            Assert.Equal(81, tx.ProductId);
        }

        [Fact]
        public void DerivedControlServerIp_NormalizesLocalhostToIPv4()
        {
            var config = new AppConfig { BavmUrl = "http://localhost:8091" };
            Assert.Equal("127.0.0.1", config.DerivedControlServerIp);

            config.BavmUrl = "http://192.168.1.200:80";
            Assert.Equal("192.168.1.200", config.DerivedControlServerIp);
        }
    }
}
