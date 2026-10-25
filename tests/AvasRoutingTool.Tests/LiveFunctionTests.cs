using System.Linq;
using System.Threading.Tasks;
using AvasRoutingTool.Sdvoe;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Live end-to-end function tests. Each test silently passes when no Control Server is reachable,
    /// so CI without hardware stays green.
    /// </summary>
    public class LiveFunctionTests
    {
        private static async Task<(SdvoeClient? client, System.Collections.Generic.List<AvasDevice> devices)> ConnectAsync()
        {
            var client = new SdvoeClient();
            if (!await client.ConnectAsync("127.0.0.1", 6970, 8090))
            {
                client.Dispose();
                return (null, new());
            }
            return (client, await client.DiscoverDevicesAsync());
        }

        [Fact]
        public async Task Discovery_ReportsStrictDeviceNamesAndRoles()
        {
            var (client, devices) = await ConnectAsync();
            if (client == null) return;
            using (client)
            {
                Assert.All(devices, d => Assert.False(string.IsNullOrWhiteSpace(d.DeviceName)));
                Assert.All(devices, d => Assert.True(d.IsTransmitter ^ d.IsReceiver, $"{d.DeviceName} must be exactly one of TX/RX"));
                var avasTx = devices.Where(d => d.IsTransmitter && d.VendorId == 105);
                Assert.All(avasTx, d => Assert.True(d.IsPreviewSupported));
                var nonAvas = devices.Where(d => d.IsTransmitter && d.VendorId != 105);
                Assert.All(nonAvas, d => Assert.False(d.IsPreviewSupported));
            }
        }

        [Fact]
        public async Task RouteThenUnroute_AllTransmitterReceiverPairs_AreAccepted()
        {
            var (client, devices) = await ConnectAsync();
            if (client == null) return;
            using (client)
            {
                var orch = new RoutingOrchestrator(client);
                foreach (var tx in devices.Where(d => d.IsTransmitter && d.IsActive))
                foreach (var rx in devices.Where(d => d.IsReceiver && d.IsActive))
                {
                    Assert.True(await orch.RouteSingleViewVideoAsync(tx, rx), $"route {tx.DeviceName} -> {rx.DeviceName}");
                    Assert.Equal(tx.DeviceId, rx.ActiveSourceId);
                    Assert.Equal(tx.DeviceName, rx.ActiveSourceName);

                    Assert.True(await orch.UnrouteDisplayAsync(rx), $"unroute {rx.DeviceName}");
                    Assert.Empty(rx.ActiveSourceId);
                }
            }
        }

        [Fact]
        public async Task AvasPreview_StartAndStop_AreAccepted()
        {
            var (client, devices) = await ConnectAsync();
            if (client == null) return;
            using (client)
            {
                foreach (var tx in devices.Where(d => d.IsTransmitter && d.IsActive && d.IsPreviewSupported))
                {
                    Assert.True(await client.StartAvasPreviewAsync(tx.DeviceId, "225.0.0.1"), $"preview start {tx.DeviceName}");
                    Assert.True(await client.StopAvasPreviewAsync(tx.DeviceId), $"preview stop {tx.DeviceName}");
                }
            }
        }
    }
}
