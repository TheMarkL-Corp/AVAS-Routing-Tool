using System.IO;
using System.Linq;
using AvasRoutingTool.Sdvoe;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public static class Fixtures
    {
        public static string Read(string name) =>
            File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "Fixtures", name));
    }

    /// <summary>Parses real (trimmed) Control Server replies captured from the lab.</summary>
    public class RxStateReaderTests
    {
        [Fact]
        public void Parse_SingleViewReceiver_ReadsSourceAndGenlockedOutput()
        {
            var state = RxStateReader.Parse(Fixtures.Read("rx_single_view.json"));

            Assert.NotNull(state);
            Assert.Equal("74fe483c059e", state!.DeviceId);
            Assert.Equal("RX1_c059e_SFPA", state.DeviceName);
            Assert.Equal("GENLOCKED", state.DisplayMode);
            Assert.False(state.IsMultiView);
            Assert.Equal((3840, 2160, 60), (state.Width, state.Height, state.Fps));

            var source = state.SingleViewSource;
            Assert.NotNull(source);
            Assert.Equal("74fe488b07bb", source!.SourceMac);
            Assert.Equal("TX2_DUAL LINK_SFPA", source.SourceName);
            Assert.Equal(0, source.SourceStream);
            Assert.Equal("STREAMING", source.State);
        }

        [Fact]
        public void Parse_UnroutedReceiver_HasNoSources()
        {
            var state = RxStateReader.Parse(Fixtures.Read("rx_unrouted.json"))!;

            Assert.False(state.IsMultiView);
            Assert.Null(state.SingleViewSource);
            Assert.Empty(state.RoutedSubscriptions);
        }

        [Fact]
        public void Parse_MultiViewReceiver_ListsEveryRoutedSubscription()
        {
            var state = RxStateReader.Parse(Fixtures.Read("rx_multiview.json"))!;

            Assert.True(state.IsMultiView);
            Assert.Null(state.SingleViewSource); // a multiview decoder has no single-view route
            var routed = state.RoutedSubscriptions.OrderBy(s => s.Index).ToList();
            Assert.Equal(2, routed.Count);
            Assert.Equal((1, "TX2_DUAL LINK_SFPA", 1), (routed[0].Index, routed[0].SourceName, routed[0].SourceStream));
            Assert.Equal((2, "VEGA_1100", 1), (routed[1].Index, routed[1].SourceName, routed[1].SourceStream));
            Assert.Equal("74fe488b07bb", routed[0].SourceMac);
        }

        [Fact]
        public void Parse_MultiViewWithLayoutReply_AttachesTheLayout()
        {
            var state = RxStateReader.Parse(Fixtures.Read("rx_multiview.json"), Fixtures.Read("rx_multiview_layout.json"))!;

            var layout = state.Layout;
            Assert.NotNull(layout);
            Assert.Equal("avas_matrix2x2_1920x1080", layout!.Name);
            Assert.Equal((1920, 1080), (layout.Width, layout.Height));
            Assert.Equal(4, layout.Windows.Count);
            Assert.Equal(new[] { (0, 1), (1, 2), (2, 3), (3, 4) }, layout.Windows.Select(w => (w.WindowIndex, w.Surface)).ToArray());
        }

        [Fact]
        public void ParseLayout_ReadsWindowGeometry()
        {
            var layout = RxStateReader.ParseLayout(Fixtures.Read("rx_multiview_layout.json"))!;

            var w3 = layout.Windows.Single(w => w.WindowIndex == 3);
            Assert.Equal((960, 540, 960, 540), (w3.X, w3.Y, w3.Width, w3.Height));
            Assert.Equal((0, 0), (w3.HOffset, w3.VOffset));

            var w1 = layout.Windows.Single(w => w.WindowIndex == 1);
            Assert.Equal((960, 0, 960, 540), (w1.X, w1.Y, w1.Width, w1.Height));
        }

        [Fact]
        public void ParseLayout_InvalidStateError_ReturnsNull()
        {
            Assert.Null(RxStateReader.ParseLayout(Fixtures.Read("rx_not_multiview_error.json")));
        }

        [Fact]
        public void Parse_LayoutReplyIsIgnoredForANonMultiViewReceiver()
        {
            var state = RxStateReader.Parse(Fixtures.Read("rx_single_view.json"), Fixtures.Read("rx_multiview_layout.json"))!;

            Assert.Null(state.Layout);
        }

        [Fact]
        public void Parse_EnabledSubscriptionWithASourceButNotStreaming_StillCountsAsRouted()
        {
            const string json = @"{""status"":""SUCCESS"",""result"":{""devices"":[{""device_id"":""aa"",""device_name"":""RX"",""nodes"":[
                {""type"":""FRAME_BUFFER"",""configuration"":{""display_mode"":""GENLOCKED"",""width"":1920,""height"":1080,""frames_per_second"":60}}],
                ""subscriptions"":[{""type"":""HDMI"",""index"":0,""configuration"":{""enable"":true},
                ""status"":{""state"":""WAITING"",""source"":{""device_id"":""bb"",""device_name"":""TX"",""stream_index"":0}}}]}],""error"":[]}}";

            var source = RxStateReader.Parse(json)!.SingleViewSource;

            Assert.NotNull(source);
            Assert.Equal("WAITING", source!.State);
        }

        [Fact]
        public void Parse_StoppedSubscriptionIsNotRouted()
        {
            const string json = @"{""status"":""SUCCESS"",""result"":{""devices"":[{""device_id"":""aa"",""device_name"":""RX"",""nodes"":[
                {""type"":""FRAME_BUFFER"",""configuration"":{""display_mode"":""GENLOCKED"",""width"":1920,""height"":1080,""frames_per_second"":60}}],
                ""subscriptions"":[{""type"":""HDMI"",""index"":0,""configuration"":{""enable"":false},""status"":{""state"":""STOPPED""}}]}],""error"":[]}}";

            Assert.Null(RxStateReader.Parse(json)!.SingleViewSource);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{}")]
        [InlineData(@"{""status"":""ERROR"",""result"":null,""error"":{""reason"":""X""}}")]
        [InlineData(@"{""status"":""SUCCESS"",""result"":{""devices"":[],""error"":[]}}")]
        public void Parse_MalformedOrEmpty_ReturnsNull(string? json)
        {
            Assert.Null(RxStateReader.Parse(json));
        }

        [Fact]
        public void Parse_NonHdmiSubscriptionsAreIgnored()
        {
            const string json = @"{""status"":""SUCCESS"",""result"":{""devices"":[{""device_id"":""aa"",""device_name"":""RX"",""nodes"":[
                {""type"":""FRAME_BUFFER"",""configuration"":{""display_mode"":""GENLOCKED"",""width"":1920,""height"":1080,""frames_per_second"":60}}],
                ""subscriptions"":[{""type"":""USB_HID"",""index"":0,""configuration"":{""enable"":true},
                ""status"":{""state"":""STREAMING"",""source"":{""device_id"":""bb"",""device_name"":""TX"",""stream_index"":0}}}]}],""error"":[]}}";

            Assert.Null(RxStateReader.Parse(json)!.SingleViewSource);
        }
    }
}
