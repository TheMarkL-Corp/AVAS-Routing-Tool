using System;
using System.Threading.Tasks;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class TemperatureReaderTests
    {
        private static string DeviceReply(string statusJson) =>
            "{\"status\":\"SUCCESS\",\"request_id\":null,\"error\":null,\"result\":{\"devices\":[{\"device_id\":\"74fe488b07bb\",\"device_name\":\"TX2\",\"status\":" + statusJson + "}]}}";

        private static SdvoeClient Client(RecordingHandler h) =>
            new(h) { PollInterval = TimeSpan.FromMilliseconds(5), RequestTimeout = TimeSpan.FromMilliseconds(400) };

        [Fact]
        public async Task ValueIsParsed_FromTheDeviceSubset()
        {
            var handler = new RecordingHandler { Responder = (m, p, b) => DeviceReply("{\"active\":true,\"temperature\":58}") };

            int? t = await new TemperatureReader(Client(handler)).ReadAsync("74FE488B07BB");

            Assert.Equal(58, t);
            var call = Assert.Single(handler.Calls);
            Assert.Equal("/api/device/74fe488b07bb", call.Path);
            Assert.Equal("get", call.Body.GetProperty("op").GetString());
            Assert.Equal("device", call.Body.GetProperty("subset").GetString());
        }

        [Fact]
        public async Task MissingValue_GivesNull()
        {
            var handler = new RecordingHandler { Responder = (m, p, b) => DeviceReply("{\"active\":true}") };
            Assert.Null(await new TemperatureReader(Client(handler)).ReadAsync("74fe488b07bb"));
        }

        [Fact]
        public async Task NullValue_GivesNull_NotZero()
        {
            var handler = new RecordingHandler { Responder = (m, p, b) => DeviceReply("{\"temperature\":null}") };
            Assert.Null(await new TemperatureReader(Client(handler)).ReadAsync("74fe488b07bb"));
        }

        [Fact]
        public async Task ServerError_GivesNull()
        {
            var handler = new RecordingHandler { FailFromCall = 1 };
            Assert.Null(await new TemperatureReader(Client(handler)).ReadAsync("74fe488b07bb"));
        }

        [Theory]
        [InlineData(null, "n/a", "Unknown")]
        [InlineData(0, "0 °C", "Normal")]
        [InlineData(60, "60 °C", "Normal")]
        [InlineData(61, "61 °C", "Warm")]
        [InlineData(70, "70 °C", "Warm")]
        [InlineData(71, "71 °C", "Hot")]
        public void TextAndStateWord(int? value, string text, string state)
        {
            var (t, s) = TemperatureFormat.Describe(value);
            Assert.Equal(text, t);
            Assert.Equal(state, s);
        }

        [Fact]
        public void RowShowsNotAvailable_WhenTemperatureIsNull()
        {
            var row = new AdvancedDeviceRowViewModel(new AvasDevice { DeviceId = "a", DeviceName = "TX", IsTransmitter = true });
            row.ApplyTemperature(null);
            Assert.Equal("n/a", row.TemperatureText);
            Assert.Equal("Unknown", row.TemperatureState);
            row.ApplyTemperature(75);
            Assert.Equal("75 °C", row.TemperatureText);
            Assert.Equal("Hot", row.TemperatureState);
        }
    }
}
