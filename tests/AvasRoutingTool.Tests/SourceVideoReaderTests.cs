using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class SourceVideoReaderTests
    {
        private static string Settings(int w, int h, int fps, string space, int depth) =>
            "{\"result\":{\"devices\":[{\"nodes\":[{\"type\":\"HDMI_DECODER\",\"status\":{\"video\":" +
            $"{{\"width\":{w},\"height\":{h},\"frames_per_second\":{fps},\"color_space\":\"{space}\",\"bits_per_pixel\":{depth}}}}}}}]}}]}}}}";

        [Fact]
        public void Parse_Rgb4k60_Is24BitsPerPixel()
        {
            var v = SourceVideoReader.Parse(Settings(3840, 2160, 60, "RGB", 8));

            Assert.Equal(new SourceVideo(3840, 2160, 60, 24), v);
        }

        [Fact]
        public void Parse_TwelveBit422_MatchesLiveBoomAt24()
        {
            var v = SourceVideoReader.Parse(Settings(3840, 2160, 60, "YCBCR_422", 12));

            Assert.Equal(24, v!.BitsPerPixel);
        }

        [Fact]
        public void Parse_NoSignal_IsNull() =>
            Assert.Null(SourceVideoReader.Parse(Settings(0, 0, 0, "RGB", 8)));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{}")]
        public void Parse_Garbage_IsNullAndNeverThrows(string? body) =>
            Assert.Null(SourceVideoReader.Parse(body));
    }
}
