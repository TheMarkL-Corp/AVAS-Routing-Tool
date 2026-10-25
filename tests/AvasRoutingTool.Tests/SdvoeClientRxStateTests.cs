using System;
using System.Linq;
using System.Threading.Tasks;
using AvasRoutingTool.Sdvoe;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class SdvoeClientRxStateTests
    {
        private static SdvoeClient FastClient(RecordingHandler handler) =>
            new(handler) { PollInterval = TimeSpan.FromMilliseconds(5), RequestTimeout = TimeSpan.FromMilliseconds(400) };

        private static string SubsetOf(System.Text.Json.JsonElement body) =>
            body.ValueKind == System.Text.Json.JsonValueKind.Object && body.TryGetProperty("subset", out var s) ? s.GetString() ?? "" : "";

        [Fact]
        public async Task SingleViewReceiver_NeedsOnlyTheSettingsRead()
        {
            var handler = new RecordingHandler { Responder = (m, p, b) => Fixtures.Read("rx_single_view.json") };

            var state = await FastClient(handler).ReadReceiverStateAsync("74FE483C059E");

            Assert.NotNull(state);
            Assert.Equal("74fe488b07bb", state!.SingleViewSource!.SourceMac);
            var call = Assert.Single(handler.Calls);
            Assert.Equal("/api/device/74fe483c059e", call.Path);
            Assert.Equal("get", call.Body.GetProperty("op").GetString());
            Assert.Equal("settings", call.Body.GetProperty("subset").GetString());
        }

        [Fact]
        public async Task MultiViewReceiver_AlsoReadsTheLoadedLayout()
        {
            var handler = new RecordingHandler
            {
                Responder = (m, p, b) => SubsetOf(b) == "multiview" ? Fixtures.Read("rx_multiview_layout.json") : Fixtures.Read("rx_multiview.json")
            };

            var state = await FastClient(handler).ReadReceiverStateAsync("74fe483c059e");

            Assert.True(state!.IsMultiView);
            Assert.Equal("avas_matrix2x2_1920x1080", state.Layout!.Name);
            Assert.Equal(new[] { "settings", "multiview" }, handler.Calls.Select(c => SubsetOf(c.Body)).ToArray());
        }

        [Fact]
        public async Task LayoutReadRefused_StillReturnsTheStateWithoutALayout()
        {
            var handler = new RecordingHandler
            {
                Responder = (m, p, b) => SubsetOf(b) == "multiview" ? Fixtures.Read("rx_not_multiview_error.json") : Fixtures.Read("rx_multiview.json")
            };

            var state = await FastClient(handler).ReadReceiverStateAsync("74fe483c059e");

            Assert.NotNull(state);
            Assert.True(state!.IsMultiView);
            Assert.Null(state.Layout);
            Assert.Equal(2, state.RoutedSubscriptions.Count());
        }

        [Fact]
        public async Task ProcessingReply_IsFollowedToTheRealResult()
        {
            var handler = new RecordingHandler
            {
                Responder = (m, p, b) => m == "POST"
                    ? "{\"status\":\"PROCESSING\",\"request_id\":8,\"result\":null,\"error\":null}"
                    : Fixtures.Read("rx_unrouted.json")
            };

            var state = await FastClient(handler).ReadReceiverStateAsync("74fe488b22ca");

            Assert.NotNull(state);
            Assert.Null(state!.SingleViewSource);
            Assert.Equal("/api/request/8", handler.Calls[^1].Path);
        }

        [Fact]
        public async Task ServerError_ReturnsNull()
        {
            var handler = new RecordingHandler { FailFromCall = 1 };

            Assert.Null(await FastClient(handler).ReadReceiverStateAsync("74fe483c059e"));
        }

        [Fact]
        public async Task GarbageReply_ReturnsNull()
        {
            var handler = new RecordingHandler { Responder = (m, p, b) => "{\"status\":\"SUCCESS\",\"result\":{\"devices\":[]}}" };

            Assert.Null(await FastClient(handler).ReadReceiverStateAsync("74fe483c059e"));
        }
    }
}
