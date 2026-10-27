using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class RecordingHandler : HttpMessageHandler
    {
        public List<(string Method, string Path, JsonElement Body)> Calls { get; } = new();

        /// <summary>1-based call number from which the server answers ERROR (0 = never).</summary>
        public int FailFromCall { get; set; }

        /// <summary>Optional scripted reply: (method, path, body) -> raw JSON, or null for the default SUCCESS.</summary>
        public System.Func<string, string, JsonElement, string?>? Responder { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content == null ? "null" : await request.Content.ReadAsStringAsync(cancellationToken);
            var json = JsonDocument.Parse(body).RootElement.Clone();
            Calls.Add((request.Method.Method, request.RequestUri!.AbsolutePath, json));

            string? scripted = Responder?.Invoke(request.Method.Method, request.RequestUri.AbsolutePath, json);
            if (scripted != null)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(scripted) };
            }

            bool fail = FailFromCall > 0 && Calls.Count >= FailFromCall;
            string status = fail ? "ERROR" : "SUCCESS";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\n   \"status\" : \"{status}\",\n   \"request_id\" : null,\n   \"result\" : null,\n   \"error\" : null\n}}")
            };
        }
    }

    public class SdvoeClientMultiViewTests
    {
        private const string LayoutName = "avas_74fe483c059e_matrix2x2_1920x1080";

        /// <summary>TX2 in blocks 1 and 3 (one shared surface), VEGA in block 2, block 4 black.</summary>
        private static (SurfacePlan Surfaces, MultiViewLayoutPlan Plan) SharedSurfaceLayout()
        {
            var draft = new MultiViewDraft(MultiViewPresets.Build("matrix2x2", 1920, 1080));
            draft.Place(0, "74fe488b07bb", "TX2");
            draft.Place(1, "0016c0589c1e", "VEGA");
            draft.Place(2, "74fe488b07bb", "TX2");
            return (SurfacePlanner.Plan(draft, "74fe483c059e"), draft.Plan);
        }

        [Fact]
        public async Task CreateMultiViewLayoutAsync_PostsTheLayoutUnderItsDisplayNameThenEveryWindowInOrder()
        {
            var handler = new RecordingHandler();
            var client = new SdvoeClient(handler);
            var (surfaces, plan) = SharedSurfaceLayout();

            bool ok = await client.CreateMultiViewLayoutAsync(surfaces, plan);

            Assert.True(ok);
            Assert.Equal(5, handler.Calls.Count);
            Assert.Equal($"/api/multiview/layout/{LayoutName}", handler.Calls[0].Path);
            Assert.Equal(1920, handler.Calls[0].Body.GetProperty("width").GetInt32());
            Assert.Equal(1080, handler.Calls[0].Body.GetProperty("height").GetInt32());
            for (int i = 0; i < 4; i++)
                Assert.Equal($"/api/multiview/layout/{LayoutName}/window/{i}", handler.Calls[i + 1].Path);
        }

        [Fact]
        public async Task CreateMultiViewLayoutAsync_WindowsShareASurface_AndAnEmptyBlockIsBlack()
        {
            var handler = new RecordingHandler();
            var (surfaces, plan) = SharedSurfaceLayout();

            await new SdvoeClient(handler).CreateMultiViewLayoutAsync(surfaces, plan);

            int? Surface(int window) => handler.Calls[window + 1].Body.TryGetProperty("target_surface", out var s) ? s.GetInt32() : null;
            Assert.Equal(new int?[] { 1, 2, 1, null }, Enumerable.Range(0, 4).Select(Surface).ToArray());
        }

        [Fact]
        public async Task CreateMultiViewLayoutAsync_StopsAtFirstRejectedCall()
        {
            var handler = new RecordingHandler { FailFromCall = 2 };
            var client = new SdvoeClient(handler);
            var (surfaces, plan) = SharedSurfaceLayout();

            bool ok = await client.CreateMultiViewLayoutAsync(surfaces, plan);

            Assert.False(ok);
            Assert.Equal(2, handler.Calls.Count);
        }

        [Fact]
        public async Task PrepareSourceScalerAsync_SetsScalerThenStartsStreamOne()
        {
            var handler = new RecordingHandler();
            var client = new SdvoeClient(handler);

            bool ok = await client.PrepareSourceScalerAsync("74fe488b07bb", 960, 540);

            Assert.True(ok);
            Assert.Equal(2, handler.Calls.Count);
            Assert.Equal("set:scaler", handler.Calls[0].Body.GetProperty("op").GetString());
            Assert.Equal("start", handler.Calls[1].Body.GetProperty("op").GetString());
            Assert.Equal(1, handler.Calls[1].Body.GetProperty("stream_index").GetInt32());
        }

        [Fact]
        public async Task PrepareSourceScalerAsync_DoesNotStartStreamWhenScalerRejected()
        {
            var handler = new RecordingHandler { FailFromCall = 1 };
            var client = new SdvoeClient(handler);

            bool ok = await client.PrepareSourceScalerAsync("74fe488b07bb", 960, 540);

            Assert.False(ok);
            Assert.Single(handler.Calls);
        }

        [Fact]
        public async Task SetMultiViewAsync_SendsOneCallWithLayoutAndSubscriptions()
        {
            var handler = new RecordingHandler();
            var client = new SdvoeClient(handler);

            bool ok = await client.SetMultiViewAsync("74fe483c059e", LayoutName, 1920, 1080, 60,
                new[] { new MultiViewSubscription("74fe488b07bb", 1) });

            Assert.True(ok);
            var call = Assert.Single(handler.Calls);
            Assert.Equal("/api/device/74fe483c059e", call.Path);
            Assert.Equal("set:multiview", call.Body.GetProperty("op").GetString());
            Assert.Equal(LayoutName, call.Body.GetProperty("layout").GetString());
            Assert.Equal(1920, call.Body.GetProperty("video").GetProperty("width").GetInt32());
            Assert.Equal(60, call.Body.GetProperty("video").GetProperty("fps").GetInt32());
        }

        private static string Processing(int id) =>
            $"{{\"status\":\"PROCESSING\",\"request_id\":{id},\"result\":null,\"error\":null}}";

        private static string Done(int id, string errorArray = "[]") =>
            $"{{\"status\":\"SUCCESS\",\"request_id\":{id},\"result\":{{\"devices\":[],\"error\":{errorArray}}},\"error\":null}}";

        private static SdvoeClient FastClient(RecordingHandler handler) =>
            new(handler) { PollInterval = System.TimeSpan.FromMilliseconds(5), RequestTimeout = System.TimeSpan.FromMilliseconds(400) };

        [Fact]
        public async Task SetMultiViewAsync_WaitsForTheBackgroundRequestToFinish()
        {
            int polls = 0;
            var handler = new RecordingHandler
            {
                Responder = (m, p, _) => m == "POST" ? Processing(9) : ++polls < 3 ? Processing(9) : Done(9)
            };
            var client = FastClient(handler);

            bool ok = await client.SetMultiViewAsync("74fe483c059e", LayoutName, 1920, 1080, 60, new[] { new MultiViewSubscription("74fe488b07bb", 1) });

            Assert.True(ok);
            Assert.Equal("/api/request/9", handler.Calls[^1].Path);
            Assert.Equal(1 + 3, handler.Calls.Count); // POST + three polls
        }

        [Fact]
        public async Task SetMultiViewAsync_ReportsFailureWhenTheDeviceRejectsItLater()
        {
            var handler = new RecordingHandler
            {
                Responder = (m, p, _) => m == "POST"
                    ? Processing(12)
                    : Done(12, "[{\"device_id\":\"74fe483c059e\",\"reason\":\"DEVICE_TYPE\"}]")
            };
            var client = FastClient(handler);

            bool ok = await client.SetMultiViewAsync("74fe483c059e", LayoutName, 1920, 1080, 60, new[] { new MultiViewSubscription("74fe488b07bb", 1) });

            Assert.False(ok);
        }

        [Fact]
        public async Task PrepareSourceScalerAsync_TimesOutWhenTheRequestNeverCompletes()
        {
            var handler = new RecordingHandler { Responder = (m, p, _) => Processing(5) };
            var client = FastClient(handler);

            bool ok = await client.PrepareSourceScalerAsync("74fe488b07bb", 960, 540);

            Assert.False(ok);
            Assert.Single(handler.Calls.Where(c => c.Method == "POST")); // never starts the stream
        }

        [Fact]
        public async Task ReadDisplayEdidAsync_ReturnsTheHexFromTheMonitorNode()
        {
            string reply = "{\"status\":\"SUCCESS\",\"request_id\":3,\"result\":{\"devices\":[{\"device_id\":\"74fe483c059e\",\"nodes\":[" +
                           "{\"type\":\"HDMI_MONITOR\",\"index\":0,\"status\":{\"connected\":true,\"edid\":\"" + TestEdids.ViewSonic4k + "\"}}]}],\"error\":[]},\"error\":null}";
            var handler = new RecordingHandler { Responder = (m, p, _) => reply };
            var client = FastClient(handler);

            string? hex = await client.ReadDisplayEdidAsync("74FE483C059E");

            Assert.Equal(TestEdids.ViewSonic4k, hex);
            var call = Assert.Single(handler.Calls);
            Assert.Equal("/api/device/74fe483c059e", call.Path);
            Assert.Equal("get", call.Body.GetProperty("op").GetString());
            Assert.Equal("edid", call.Body.GetProperty("subset").GetString());
        }

        [Fact]
        public async Task ReadDisplayEdidAsync_PollsWhenTheServerAnswersProcessing()
        {
            string reply = "{\"status\":\"SUCCESS\",\"request_id\":3,\"result\":{\"devices\":[{\"nodes\":[" +
                           "{\"type\":\"HDMI_MONITOR\",\"status\":{\"connected\":true,\"edid\":\"" + TestEdids.Pax355s + "\"}}]}],\"error\":[]},\"error\":null}";
            var handler = new RecordingHandler { Responder = (m, p, _) => m == "POST" ? Processing(3) : reply };
            var client = FastClient(handler);

            Assert.Equal(TestEdids.Pax355s, await client.ReadDisplayEdidAsync("74fe488b22ca"));
            Assert.Equal("/api/request/3", handler.Calls[^1].Path);
        }

        [Fact]
        public async Task ReadDisplayEdidAsync_NoDisplayConnected_ReturnsNull()
        {
            string reply = "{\"status\":\"SUCCESS\",\"request_id\":3,\"result\":{\"devices\":[{\"nodes\":[" +
                           "{\"type\":\"HDMI_MONITOR\",\"status\":{\"connected\":false,\"edid\":\"" + TestEdids.ViewSonic4k + "\"}}]}],\"error\":[]},\"error\":null}";
            var handler = new RecordingHandler { Responder = (m, p, _) => reply };

            Assert.Null(await FastClient(handler).ReadDisplayEdidAsync("74fe483c059e"));
        }

        [Fact]
        public async Task ReadDisplayEdidAsync_ServerError_ReturnsNull()
        {
            var handler = new RecordingHandler { FailFromCall = 1 };

            Assert.Null(await FastClient(handler).ReadDisplayEdidAsync("74fe483c059e"));
        }

        [Theory]
        [InlineData(true, 1)]
        [InlineData(false, 0)]
        public async Task SetStream1FrameDividerAsync_SendsThePropertyAndWaitsForTheResult(bool halve, int value)
        {
            var handler = new RecordingHandler
            {
                Responder = (m, p, _) => m == "POST" ? Processing(21) : Done(21)
            };
            var client = FastClient(handler);

            bool ok = await client.SetStream1FrameDividerAsync("74fe488b07bb", halve);

            Assert.True(ok);
            Assert.Equal("set:property", handler.Calls[0].Body.GetProperty("op").GetString());
            Assert.Equal(value, handler.Calls[0].Body.GetProperty("value").GetInt32());
            Assert.Equal("/api/request/21", handler.Calls[^1].Path);
        }

        private static string SettingsWithScalerSource(string sourceJson) =>
            "{\"status\":\"SUCCESS\",\"request_id\":null,\"result\":{\"devices\":[{\"nodes\":[" +
            "{\"type\":\"HDMI_DECODER\",\"index\":0}," +
            "{\"type\":\"SCALER\",\"index\":0,\"configuration\":{\"width\":1920,\"height\":1080}," +
            "\"inputs\":[{\"name\":\"main\",\"index\":0,\"configuration\":" + sourceJson + "}]}]}],\"error\":[]},\"error\":null}";

        [Theory]
        [InlineData("{\"source\":{\"value\":1}}", true)]
        [InlineData("{\"source\":{\"value\":0}}", false)]
        [InlineData("{}", null)]
        public async Task ReadStream1FrameDividerAsync_ReadsTheScalerInputSource(string sourceJson, bool? expected)
        {
            var handler = new RecordingHandler { Responder = (m, p, _) => SettingsWithScalerSource(sourceJson) };

            bool? halved = await FastClient(handler).ReadStream1FrameDividerAsync("74fe488b07bb");

            Assert.Equal(expected, halved);
            Assert.Equal("settings", handler.Calls[0].Body.GetProperty("subset").GetString());
        }

        [Fact]
        public async Task ReadStream1FrameDividerAsync_ServerError_ReturnsNull()
        {
            var handler = new RecordingHandler { FailFromCall = 1 };

            Assert.Null(await FastClient(handler).ReadStream1FrameDividerAsync("74fe488b07bb"));
        }

        [Fact]
        public async Task SetStream1FrameDividerAsync_DeviceWithoutDivider_ReportsFailure()
        {
            var handler = new RecordingHandler
            {
                Responder = (m, p, _) => m == "POST"
                    ? Processing(22)
                    : Done(22, "[{\"device_id\":\"x\",\"reason\":\"DEVICE_TYPE\"}]")
            };

            Assert.False(await FastClient(handler).SetStream1FrameDividerAsync("74fe488b07bb", true));
        }
        [Fact]
        public async Task SetFastSwitchedAsync_SendsSetVideoFastSwitched()
        {
            var handler = new RecordingHandler();
            var client = new SdvoeClient(handler);

            bool ok = await client.SetFastSwitchedAsync("74fe483c059e", 3840, 2160, 60);

            Assert.True(ok);
            var call = Assert.Single(handler.Calls);
            Assert.Equal("FAST_SWITCHED", call.Body.GetProperty("display_mode").GetString());
            Assert.Equal(3840, call.Body.GetProperty("video").GetProperty("width").GetInt32());
            Assert.Equal(60, call.Body.GetProperty("video").GetProperty("fps").GetInt32());
        }
    }
}
