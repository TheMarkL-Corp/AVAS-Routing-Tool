using System.Linq;
using System.Text.Json;
using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>Request shapes are pinned to PDS-062489 (SDVoE API Reference 3.9) §5.2 and §5.4 examples.</summary>
    public class MultiViewRequestsTests
    {
        private static JsonElement Json(RestCall call) =>
            JsonDocument.Parse(JsonSerializer.Serialize(call.Body)).RootElement;

        [Fact]
        public void CreateLayout_PostsNameInPathWithCanvasSize()
        {
            var call = MultiViewRequests.CreateLayout("avas_74fe483c059e_matrix2x2_1920x1080", 1920, 1080);

            Assert.Equal("POST", call.Method);
            Assert.Equal("/api/multiview/layout/avas_74fe483c059e_matrix2x2_1920x1080", call.Path);
            var body = Json(call);
            Assert.Equal("create", body.GetProperty("op").GetString());
            Assert.Equal(1920, body.GetProperty("width").GetInt32());
            Assert.Equal(1080, body.GetProperty("height").GetInt32());
        }

        [Fact]
        public void CreateWindow_UsesApiIndexPositionSizeAndTargetSurface()
        {
            var slot = MultiViewPresets.Build("matrix2x2", 1920, 1080).Slots[3];

            var call = MultiViewRequests.CreateWindow("avas_74fe483c059e_matrix2x2_1920x1080", slot, 2);

            Assert.Equal("POST", call.Method);
            Assert.Equal("/api/multiview/layout/avas_74fe483c059e_matrix2x2_1920x1080/window/3", call.Path);
            var body = Json(call);
            Assert.Equal("create", body.GetProperty("op").GetString());
            Assert.Equal(960, body.GetProperty("horizontal_position").GetInt32());
            Assert.Equal(540, body.GetProperty("vertical_position").GetInt32());
            Assert.Equal(960, body.GetProperty("width").GetInt32());
            Assert.Equal(540, body.GetProperty("height").GetInt32());
            Assert.Equal(0, body.GetProperty("horizontal_offset").GetInt32());
            Assert.Equal(0, body.GetProperty("vertical_offset").GetInt32());
            Assert.Equal(2, body.GetProperty("target_surface").GetInt32());
        }

        [Fact]
        public void CreateWindow_WithSurface_UsesTheGivenLayoutNameAndSurface()
        {
            var slot = MultiViewPresets.Build("matrix2x2", 1920, 1080).Slots[1];

            var call = MultiViewRequests.CreateWindow("avas_74fe483c059e_matrix2x2_1920x1080", slot, 5);

            Assert.Equal("/api/multiview/layout/avas_74fe483c059e_matrix2x2_1920x1080/window/1", call.Path);
            Assert.Equal(5, Json(call).GetProperty("target_surface").GetInt32());
        }

        [Fact]
        public void CreateWindow_WithoutSurface_OmitsTargetSurface()
        {
            var slot = MultiViewPresets.Build("matrix2x2", 1920, 1080).Slots[1];

            var call = MultiViewRequests.CreateWindow("avas_74fe483c059e_matrix2x2_1920x1080", slot, null);

            var body = Json(call);
            Assert.False(body.TryGetProperty("target_surface", out _));
            Assert.Equal(960, body.GetProperty("width").GetInt32());
        }

        [Fact]
        public void SetScaler_NormalisesMacAndSendsWindowSize()
        {
            var call = MultiViewRequests.SetScaler("74:FE:48:8B:07:BB", 960, 540);

            Assert.Equal("POST", call.Method);
            Assert.Equal("/api/device/74fe488b07bb", call.Path);
            var body = Json(call);
            Assert.Equal("set:scaler", body.GetProperty("op").GetString());
            Assert.Equal(960, body.GetProperty("width").GetInt32());
            Assert.Equal(540, body.GetProperty("height").GetInt32());
        }

        [Fact]
        public void StartScaledStream_StartsHdmiStreamIndexOne()
        {
            var call = MultiViewRequests.StartScaledStream("74fe488b07bb");

            Assert.Equal("/api/device/74fe488b07bb", call.Path);
            var body = Json(call);
            Assert.Equal("start", body.GetProperty("op").GetString());
            Assert.Equal("HDMI", body.GetProperty("stream_type").GetString());
            Assert.Equal(1, body.GetProperty("stream_index").GetInt32());
        }

        [Fact]
        public void SetMultiView_CarriesLayoutVideoFormatAndSubscriptionsOnStreamOne()
        {
            var subs = new[]
            {
                new MultiViewSubscription("74fe488b07bb", 1),
                new MultiViewSubscription("74fe488b07bc", 2),
            };

            var call = MultiViewRequests.SetMultiView("74fe483c059e", "avas_matrix2x2_1920x1080", 1920, 1080, 60, subs);

            Assert.Equal("POST", call.Method);
            Assert.Equal("/api/device/74fe483c059e", call.Path);
            var body = Json(call);
            Assert.Equal("set:multiview", body.GetProperty("op").GetString());
            Assert.Equal("avas_matrix2x2_1920x1080", body.GetProperty("layout").GetString());

            var video = body.GetProperty("video");
            Assert.Equal(1920, video.GetProperty("width").GetInt32());
            Assert.Equal(1080, video.GetProperty("height").GetInt32());
            Assert.Equal(60, video.GetProperty("fps").GetInt32());

            var arr = body.GetProperty("subscriptions").EnumerateArray().ToList();
            Assert.Equal(2, arr.Count);
            Assert.Equal("74fe488b07bb", arr[0].GetProperty("source_device").GetString());
            Assert.Equal(1, arr[0].GetProperty("stream_index").GetInt32());
            Assert.Equal(1, arr[0].GetProperty("subscription_index").GetInt32());
            Assert.Equal(2, arr[1].GetProperty("subscription_index").GetInt32());
        }

        [Fact]
        public void SetGenlocked_SendsDisplayModeOnly()
        {
            var call = MultiViewRequests.SetGenlocked("74fe483c059e");

            Assert.Equal("/api/device/74fe483c059e", call.Path);
            var body = Json(call);
            Assert.Equal("set:video", body.GetProperty("op").GetString());
            Assert.Equal("GENLOCKED", body.GetProperty("display_mode").GetString());
            // Genlock takes no video format arguments (API 4.19.12.1).
            Assert.False(body.TryGetProperty("video", out _));
            Assert.False(body.TryGetProperty("width", out _));
        }

        [Theory]
        [InlineData(true, 1)]
        [InlineData(false, 0)]
        public void SetScalerFrameDivider_SetsTheScalerInputSource(bool halve, int expectedValue)
        {
            var call = MultiViewRequests.SetScalerFrameDivider("74:FE:48:8B:07:BB", halve);

            Assert.Equal("POST", call.Method);
            Assert.Equal("/api/device/74fe488b07bb", call.Path);
            var body = Json(call);
            Assert.Equal("set:property", body.GetProperty("op").GetString());
            Assert.Equal("nodes[SCALER:0].inputs[main:0].configuration.source.value", body.GetProperty("key").GetString());
            Assert.Equal(expectedValue, body.GetProperty("value").GetInt32());
        }
    }
}
