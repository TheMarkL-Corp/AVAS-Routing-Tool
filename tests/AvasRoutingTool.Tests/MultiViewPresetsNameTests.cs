using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class MultiViewPresetsNameTests
    {
        [Theory]
        [InlineData("avas_matrix2x2_3840x2160", "matrix2x2", 3840, 2160)]
        [InlineData("avas_pip_1920x1080", "pip", 1920, 1080)]
        [InlineData("avas_side_by_side_1920x1200", "side_by_side", 1920, 1200)]
        [InlineData("avas_quad_1_3_3840x2160", "quad_1_3", 3840, 2160)]
        public void TryParseName_RecognisesLayoutsThisToolCreated(string name, string id, int w, int h)
        {
            Assert.True(MultiViewPresets.TryParseName(name, out var parsedId, out int pw, out int ph));
            Assert.Equal((id, w, h), (parsedId, pw, ph));
        }

        [Theory]
        [InlineData("avas_74fe483c059e_matrix2x2_1920x1080")]
        [InlineData("avas_matrix2x2_1920x1080")]
        public void TryParseName_PerDisplayAndLegacyNames(string name)
        {
            Assert.True(MultiViewPresets.TryParseName(name, out var id, out int w, out int h));
            Assert.Equal(("matrix2x2", 1920, 1080), (id, w, h));
        }

        [Fact]
        public void LayoutName_EmbedsTheNormalisedReceiverMac()
        {
            Assert.Equal("avas_74fe483c059e_side_by_side_1920x1200",
                MultiViewPresets.LayoutName("side_by_side", 1920, 1200, "74:FE:48:3C:05:9E"));
        }

        [Fact]
        public void TryParseName_RoundTripsPerDisplayNames()
        {
            foreach (var id in MultiViewPresets.Ids)
            {
                string name = MultiViewPresets.LayoutName(id, 3840, 2160, "74:FE:48:3C:05:9E");
                Assert.True(MultiViewPresets.TryParseName(name, out var parsed, out int w, out int h));
                Assert.Equal((id, 3840, 2160), (parsed, w, h));
            }
        }

        [Theory]
        [InlineData("avas_74fe483c059_matrix2x2_1920x1080")]
        [InlineData("avas_74fe483c059g_matrix2x2_1920x1080")]
        [InlineData("avas_74fe483c059e_unknown_1920x1080")]
        [InlineData("avas_74fe483c059e_matrix2x2_1920")]
        public void TryParseName_RejectsMalformedPerDisplayNames(string name)
        {
            Assert.False(MultiViewPresets.TryParseName(name, out _, out _, out _));
        }

        [Theory]
        [InlineData("compatibility_4k_2x2")]
        [InlineData("example_4k_single_source_2x2_1080p")]
        [InlineData("avas_unknown_1920x1080")]
        [InlineData("avas_matrix2x2_1920")]
        [InlineData("avas_matrix2x2_axb")]
        [InlineData("")]
        [InlineData(null)]
        public void TryParseName_RejectsEverythingElse(string? name)
        {
            Assert.False(MultiViewPresets.TryParseName(name, out _, out _, out _));
        }

        [Fact]
        public void TryParseName_RoundTripsEveryPresetName()
        {
            foreach (var id in MultiViewPresets.Ids)
            {
                var plan = MultiViewPresets.Build(id, 3840, 2160);
                Assert.True(MultiViewPresets.TryParseName(plan.Name, out var parsed, out int w, out int h));
                Assert.Equal((id, 3840, 2160), (parsed, w, h));
            }
        }
    }
}
