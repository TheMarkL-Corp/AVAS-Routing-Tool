using System.IO;
using AvasRoutingTool.Configuration;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class ConfigServiceTests
    {
        [Fact]
        public void AppConfig_DerivedControlServerIp_ExtractsHostFromBavmUrl()
        {
            var config = new AppConfig { BavmUrl = "http://192.168.1.200:80" };
            Assert.Equal("192.168.1.200", config.DerivedControlServerIp);

            config.BavmUrl = "http://avas-server:8080/spa";
            Assert.Equal("avas-server", config.DerivedControlServerIp);
        }

        [Fact]
        public void Load_WhenFileDoesNotExist_ReturnsDefaultsAndPersists()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"cfg_{System.Guid.NewGuid()}.json");
            try
            {
                var service = new ConfigService(tempFile);
                var config = service.Load();

                Assert.NotNull(config);
                Assert.Equal("http://localhost:80", config.BavmUrl);
                Assert.Equal("localhost", config.DerivedControlServerIp);
                Assert.Equal(6970, config.TelnetPort);
                Assert.Equal(8080, config.RestPort);
                Assert.True(File.Exists(tempFile));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}
