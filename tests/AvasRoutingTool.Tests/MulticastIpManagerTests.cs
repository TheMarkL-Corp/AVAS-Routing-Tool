using AvasRoutingTool.Sdvoe;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class MulticastIpManagerTests
    {
        [Fact]
        public void AllocateIp_AssignsSequentialUniqueIps()
        {
            var manager = new MulticastIpManager("225.0.0.1", "225.0.0.3");
            string ip1 = manager.AllocateIp("00:11:22:33:44:01");
            string ip2 = manager.AllocateIp("00:11:22:33:44:02");
            string ip3 = manager.AllocateIp("00:11:22:33:44:03");

            Assert.Equal("225.0.0.1", ip1);
            Assert.Equal("225.0.0.2", ip2);
            Assert.Equal("225.0.0.3", ip3);
        }

        [Fact]
        public void AllocateIp_WhenSameMacReAllocates_ReturnsSameIp()
        {
            var manager = new MulticastIpManager("225.0.0.1", "225.0.0.5");
            string ip1 = manager.AllocateIp("00:11:22:33:44:01");
            string ip2 = manager.AllocateIp("00:11:22:33:44:01");

            Assert.Equal(ip1, ip2);
        }

        [Fact]
        public void ReleaseIp_RecyclesIpForNextDevice()
        {
            var manager = new MulticastIpManager("225.0.0.1", "225.0.0.1");
            string ip1 = manager.AllocateIp("00:11:22:33:44:01");
            manager.ReleaseIp("00:11:22:33:44:01");

            string ip2 = manager.AllocateIp("00:11:22:33:44:02");
            Assert.Equal(ip1, ip2);
        }
    }
}
