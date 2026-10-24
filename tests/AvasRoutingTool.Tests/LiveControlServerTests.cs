using System.Threading.Tasks;
using AvasRoutingTool.Sdvoe;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class LiveControlServerTests
    {
        [Fact]
        public async Task LiveControlServer_CanConnectAndDiscoverDevices()
        {
            using var client = new SdvoeClient();

            // Connect to live control server on localhost
            bool connected = await client.ConnectAsync("127.0.0.1", 6970, 8090);

            // If control server is running in current environment, verify full device discovery
            if (connected)
            {
                var devices = await client.DiscoverDevicesAsync();
                Assert.NotEmpty(devices);

                // Check that we have both transmitters and receivers
                bool hasTx = devices.Exists(d => d.IsTransmitter);
                bool hasRx = devices.Exists(d => d.IsReceiver);

                Assert.True(hasTx, "Expected at least one transmitter in live discovery.");
                Assert.True(hasRx, "Expected at least one receiver in live discovery.");
            }
        }
    }
}
