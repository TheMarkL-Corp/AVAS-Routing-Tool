using System.Collections.Generic;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class ScreenStateTests
    {
        private const string TxMac = "74fe488b07bb";
        private const string TxName = "TX2_DUAL LINK_SFPA";
        private const string VegaMac = "74fe48000001";
        private const string VegaName = "VEGA_1100";
        private const string CamMac = "74fe48000002";
        private const string CamName = "CAM_1";

        private static readonly IReadOnlyList<RxSubscription> NoSubs = new List<RxSubscription>();

        private static ScreenState MultiView(MultiViewDraft? draft) =>
            new(ScreenKind.MultiView, 3840, 2160, 60, null, null, draft, null, NoSubs);

        private static MultiViewDraft Draft(string preset) => new(MultiViewPresets.Build(preset, 3840, 2160));

        private static RxSubscription Sub(int index, string mac, string name) =>
            new(index, true, "STREAMING", mac, name, 1);

        private static ScreenState External(RxLayout? layout, params RxSubscription[] subs) =>
            new(ScreenKind.External, 3840, 2160, 60, null, null, null, layout, subs);

        [Theory]
        [InlineData("matrix2x2", "Matrix 2x2")]
        [InlineData("pip", "PIP")]
        [InlineData("side_by_side", "Side-by-Side")]
        [InlineData("quad_1_3", "1+3 Quad")]
        public void MultiView_UsesThePresetTitle(string preset, string title)
        {
            var draft = Draft(preset);
            Assert.True(draft.Place(0, TxMac, TxName).Ok);

            Assert.Equal($"{title} ({TxName}) - 3840x2160", MultiView(draft).Describe());
        }

        [Fact]
        public void MultiView_WithAnUnknownPlanId_FallsBackToThePlanName()
        {
            var built = MultiViewPresets.Build("pip", 3840, 2160);
            var draft = new MultiViewDraft(new MultiViewLayoutPlan("custom", "custom_layout", 3840, 2160, built.Slots));
            Assert.True(draft.Place(0, TxMac, TxName).Ok);

            Assert.Equal($"custom_layout ({TxName}) - 3840x2160", MultiView(draft).Describe());
        }

        [Fact]
        public void MultiView_NamesSourcesInBlockOrder_NotPlacementOrder()
        {
            var draft = Draft("matrix2x2");
            Assert.True(draft.Place(1, VegaMac, VegaName).Ok);
            Assert.True(draft.Place(0, TxMac, TxName).Ok);

            Assert.Equal($"Matrix 2x2 ({TxName}, {VegaName}) - 3840x2160", MultiView(draft).Describe());
        }

        [Fact]
        public void MultiView_ListsASourceOnceEvenWhenItFillsSeveralBlocks()
        {
            var draft = Draft("matrix2x2");
            Assert.True(draft.Place(0, TxMac, TxName).Ok);
            Assert.True(draft.Place(1, VegaMac, VegaName).Ok);
            Assert.True(draft.Place(2, TxMac.ToUpperInvariant(), TxName).Ok);

            Assert.Equal($"Matrix 2x2 ({TxName}, {VegaName}) - 3840x2160", MultiView(draft).Describe());
        }

        [Fact]
        public void MultiView_WithNoSources_OmitsTheParentheses()
        {
            Assert.Equal("Matrix 2x2 - 3840x2160", MultiView(Draft("matrix2x2")).Describe());
        }

        [Fact]
        public void MultiView_WithoutADraft_IsStillDescribed()
        {
            Assert.Equal("MultiView - 3840x2160", MultiView(null).Describe());
        }

        [Fact]
        public void Nothing_IsDescribedAsNothing()
        {
            var state = new ScreenState(ScreenKind.Nothing, 0, 0, 0, null, null, null, null, NoSubs);

            Assert.Equal("Nothing", state.Describe());
        }

        [Fact]
        public void Single_NamesItsSource()
        {
            var state = new ScreenState(ScreenKind.Single, 1920, 1080, 60, TxMac, TxName, null, null, NoSubs);

            Assert.Equal($"Single View ({TxName}) - 1920x1080", state.Describe());
        }

        [Fact]
        public void External_NamesOnlyTheSourcesItsWindowsShow_InWindowOrder()
        {
            var layout = new RxLayout("compat", 3840, 2160, new[]
            {
                new RxWindow(2, 2, 0, 0, 1920, 1080),
                new RxWindow(0, 1, 1920, 0, 1920, 1080),
            });

            var state = External(layout,
                Sub(1, TxMac, TxName),
                Sub(2, VegaMac, VegaName),
                Sub(3, CamMac, CamName)); // routed but no window shows surface 3

            Assert.Equal($"External layout ({TxName}, {VegaName}) - 3840x2160", state.Describe());
        }

        [Fact]
        public void External_SkipsAWindowWhoseSurfaceHasNoRoutedSubscription()
        {
            var layout = new RxLayout("compat", 3840, 2160, new[]
            {
                new RxWindow(0, 1),
                new RxWindow(1, 255), // black window
                new RxWindow(2, 2),
            });

            var state = External(layout, Sub(1, TxMac, TxName), Sub(2, VegaMac, VegaName));

            Assert.Equal($"External layout ({TxName}, {VegaName}) - 3840x2160", state.Describe());
        }

        [Fact]
        public void External_DedupesBySourceMac_NotByName()
        {
            var layout = new RxLayout("compat", 3840, 2160, new[]
            {
                new RxWindow(0, 1),
                new RxWindow(1, 2),
                new RxWindow(2, 3),
            });

            // Same encoder on surfaces 1 and 2 (mac written differently), a different encoder that shares the name.
            var state = External(layout,
                Sub(1, TxMac, TxName),
                Sub(2, TxMac.ToUpperInvariant(), TxName),
                Sub(3, VegaMac, TxName));

            Assert.Equal($"External layout ({TxName}, {TxName}) - 3840x2160", state.Describe());
        }

        [Fact]
        public void External_WithoutALayout_FallsBackToEveryRoutedSubscription()
        {
            var state = External(null, Sub(2, VegaMac, VegaName), Sub(1, TxMac, TxName));

            Assert.Equal($"External layout ({TxName}, {VegaName}) - 3840x2160", state.Describe());
        }

        [Fact]
        public void External_ShowingNoRoutedSource_OmitsTheParentheses()
        {
            var layout = new RxLayout("compat", 3840, 2160, new[] { new RxWindow(0, 255) });

            Assert.Equal("External layout - 3840x2160", External(layout).Describe());
        }
    }
}
