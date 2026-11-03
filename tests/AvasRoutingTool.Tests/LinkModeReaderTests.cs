using System;
using System.Threading.Tasks;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class LinkModeReaderTests
    {
        private static string Reply(string mode, string companion = "", string caps = "") =>
            "{\"status\":\"SUCCESS\",\"request_id\":null,\"error\":null,\"result\":{\"devices\":[{\"device_id\":\"74fe488b07bb\",\"nodes\":[" +
            "{\"type\":\"HDMI_INPUT\"},{\"type\":\"MULTI_LINK_TRANSMITTER\",\"configuration\":{\"link_mode\":\"" + mode + "\"},\"status\":{" +
            (companion == "" ? "\"companions\":[]" : "\"companions\":[{\"device_id\":\"" + companion + "\"}]") +
            (caps == "" ? "" : ",\"link_capabilities\":[" + caps + "]") + "}}]}]}}";

        private static SdvoeClient Client(RecordingHandler h) =>
            new(h) { PollInterval = TimeSpan.FromMilliseconds(5), RequestTimeout = TimeSpan.FromMilliseconds(400) };

        [Fact]
        public async Task Single_WithCapabilities()
        {
            var h = new RecordingHandler { Responder = (m, p, b) => Reply("SINGLE", "", "{\"mode\":\"SINGLE\"},{\"mode\":\"DUAL\"}") };

            var info = await new LinkModeReader(Client(h)).ReadAsync("74FE488B07BB");

            Assert.Equal(LinkMode.Single, info.Mode);
            Assert.False(info.HasCompanion);
            Assert.Equal("SINGLE,DUAL", info.Capabilities);
            Assert.Equal("/api/device/74fe488b07bb", Assert.Single(h.Calls).Path);
            Assert.Equal("device", h.Calls[0].Body.GetProperty("subset").GetString());
        }

        [Fact]
        public async Task Dual_WithCompanion()
        {
            var h = new RecordingHandler { Responder = (m, p, b) => Reply("DUAL", "74fe488b07bc") };

            var info = await new LinkModeReader(Client(h)).ReadAsync("74fe488b07bb");

            Assert.Equal(LinkMode.Dual, info.Mode);
            Assert.True(info.HasCompanion);
            Assert.Equal("74fe488b07bc", info.CompanionMac);
        }

        [Fact]
        public async Task NoMultiLinkNode_IsUnknown()
        {
            var h = new RecordingHandler { Responder = (m, p, b) => "{\"status\":\"SUCCESS\",\"request_id\":null,\"error\":null,\"result\":{\"devices\":[{\"nodes\":[{\"type\":\"HDMI_INPUT\"}]}]}}" };
            Assert.Equal(LinkMode.Unknown, (await new LinkModeReader(Client(h)).ReadAsync("74fe488b07bb")).Mode);
        }

        [Fact]
        public async Task ServerError_IsUnknown()
        {
            var h = new RecordingHandler { FailFromCall = 1 };
            var info = await new LinkModeReader(Client(h)).ReadAsync("74fe488b07bb");
            Assert.Equal(LinkMode.Unknown, info.Mode);
            Assert.False(info.HasCompanion);
        }

        [Theory]
        [InlineData(LinkMode.Single, "Single link")]
        [InlineData(LinkMode.Dual, "Dual link")]
        [InlineData(LinkMode.Unknown, "n/a")]
        public void RowText(LinkMode mode, string text)
        {
            var row = new AdvancedDeviceRowViewModel(new AvasDevice { DeviceId = "a", DeviceName = "TX", IsTransmitter = true });
            row.ApplyLinkMode(new LinkModeInfo(mode, "NONE", ""));
            Assert.Equal(text, row.LinkMode);
        }
    }
}
