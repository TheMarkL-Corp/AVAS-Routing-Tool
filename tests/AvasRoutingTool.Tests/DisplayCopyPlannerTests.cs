using System.Collections.Generic;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class DisplayCopyPlannerTests
    {
        private const string Source = "RX1_c059e_SFPA";
        private const string Target = "PAX355S_SFP A";
        private const string TxMac = "74fe488b07bb";
        private const string TxName = "TX2_DUAL LINK_SFPA";
        private const string VegaMac = "74fe48000001";
        private const string VegaName = "VEGA_1100";

        private static readonly EdidInfo FourK = EdidInfo.Parse(TestEdids.ViewSonic4k)!;
        private static readonly EdidInfo FullHdOnly = EdidInfo.Parse(TestEdids.Build((1920, 1080, 60, false)))!;

        private static ScreenState Matrix(int width = 3840, int height = 2160, int fps = 60)
        {
            var draft = new MultiViewDraft(MultiViewPresets.Build("matrix2x2", width, height));
            Assert.True(draft.Place(0, TxMac, TxName).Ok);
            Assert.True(draft.Place(1, VegaMac, VegaName).Ok);
            Assert.True(draft.Place(2, TxMac, TxName).Ok);
            return new ScreenState(ScreenKind.MultiView, width, height, fps, null, null, draft, null, new List<RxSubscription>());
        }

        private static ScreenState Single(int width, int height, int fps = 60) =>
            new(ScreenKind.Single, width, height, fps, TxMac, TxName, null, null, new List<RxSubscription>());

        private static ScreenState Nothing() =>
            new(ScreenKind.Nothing, 0, 0, 0, null, null, null, null, new List<RxSubscription>());

        [Fact]
        public void SameNativeResolution_IsAllowed()
        {
            var d = DisplayCopyPlanner.Decide(Source, Matrix(), Target, true, false, FourK);

            Assert.True(d.Allowed);
            Assert.Equal("Copy RX1_c059e_SFPA - Matrix 2x2 (TX2_DUAL LINK_SFPA, VEGA_1100) - 3840x2160", d.Summary);
            Assert.Null(d.Note);
            Assert.Null(d.Reason);
        }

        [Fact]
        public void HigherNativeResolution_IsAllowed_AtTheSourcesFormat()
        {
            var d = DisplayCopyPlanner.Decide(Source, Matrix(1920, 1080, 60), Target, true, false, FourK);

            Assert.True(d.Allowed);
            Assert.Equal("Runs at 1920x1080 @ 60 on this 3840x2160 display", d.Note);
            Assert.Null(d.Reason);
        }

        [Fact]
        public void LowerNativeResolution_IsBlocked()
        {
            var d = DisplayCopyPlanner.Decide(Source, Matrix(), Target, true, false, FullHdOnly);

            Assert.False(d.Allowed);
            Assert.Equal("PAX355S_SFP A is 1920x1080 - RX1_c059e_SFPA's picture is 3840x2160. Copy needs a display at least that large.", d.Reason);
            Assert.Equal("Copy RX1_c059e_SFPA - Matrix 2x2 (TX2_DUAL LINK_SFPA, VEGA_1100) - 3840x2160", d.Summary);
            Assert.Null(d.Note);
        }

        [Fact]
        public void ModeNotListedInTheTargetEdid_IsBlocked()
        {
            var d = DisplayCopyPlanner.Decide(Source, Single(2560, 1440), Target, true, false, FourK);

            Assert.False(d.Allowed);
            Assert.Equal("PAX355S_SFP A does not list 2560x1440 @ 60 in its EDID", d.Reason);
        }

        [Fact]
        public void UnreadableTargetEdid_IsBlocked()
        {
            var d = DisplayCopyPlanner.Decide(Source, Matrix(), Target, true, false, null);

            Assert.False(d.Allowed);
            Assert.Equal("Can't read PAX355S_SFP A's resolution - copy blocked to be safe", d.Reason);
        }

        [Fact]
        public void TargetEdidWithoutANativeMode_IsBlocked()
        {
            var noTimings = EdidInfo.Parse(TestEdids.Build())!;

            var d = DisplayCopyPlanner.Decide(Source, Matrix(), Target, true, false, noTimings);

            Assert.False(d.Allowed);
            Assert.Equal("Can't read PAX355S_SFP A's resolution - copy blocked to be safe", d.Reason);
        }

        [Fact]
        public void OfflineTarget_IsBlocked()
        {
            var d = DisplayCopyPlanner.Decide(Source, Matrix(), Target, false, false, FourK);

            Assert.False(d.Allowed);
            Assert.Equal("PAX355S_SFP A is offline", d.Reason);
        }

        [Fact]
        public void SourceShowingNothing_IsBlocked()
        {
            var d = DisplayCopyPlanner.Decide(Source, Nothing(), Target, true, false, FourK);

            Assert.False(d.Allowed);
            Assert.Equal("RX1_c059e_SFPA shows nothing to copy", d.Reason);
            Assert.Equal(string.Empty, d.Summary);
        }

        [Fact]
        public void DroppingOnItself_IsNotACopy()
        {
            var d = DisplayCopyPlanner.Decide(Source, Matrix(), Source, true, true, FourK);

            Assert.False(d.Allowed);
            Assert.Equal("Drop on another display", d.Reason);
            Assert.Equal(string.Empty, d.Summary);
        }

        [Fact]
        public void DroppingNothingOnItself_ReportsTheSelfDropFirst()
        {
            var d = DisplayCopyPlanner.Decide(Source, Nothing(), Source, false, true, null);

            Assert.Equal("Drop on another display", d.Reason);
        }

        [Fact]
        public void SingleViewSource_UsesTheSameResolutionRule()
        {
            var d = DisplayCopyPlanner.Decide(Source, Single(3840, 2160), Target, true, false, FullHdOnly);

            Assert.False(d.Allowed);
            Assert.Equal("PAX355S_SFP A is 1920x1080 - RX1_c059e_SFPA's picture is 3840x2160. Copy needs a display at least that large.", d.Reason);
            Assert.Equal("Copy RX1_c059e_SFPA - Single View (TX2_DUAL LINK_SFPA) - 3840x2160", d.Summary);
        }

        [Fact]
        public void SingleViewSource_OnASameSizeDisplay_IsAllowed()
        {
            var d = DisplayCopyPlanner.Decide(Source, Single(3840, 2160), Target, true, false, FourK);

            Assert.True(d.Allowed);
            Assert.Equal("Copy RX1_c059e_SFPA - Single View (TX2_DUAL LINK_SFPA) - 3840x2160", d.Summary);
            Assert.Null(d.Note);
        }

        [Fact]
        public void ExternalLayout_IsCopyable()
        {
            var layout = new RxLayout("compatibility_4k_2x2", 3840, 2160, new[]
            {
                new RxWindow(0, 1, 0, 0, 1920, 1080),
                new RxWindow(1, 2, 1920, 0, 1920, 1080),
            });
            var subs = new[]
            {
                new RxSubscription(1, true, "STREAMING", TxMac, TxName, 1),
                new RxSubscription(2, true, "STREAMING", VegaMac, VegaName, 1),
            };
            var external = new ScreenState(ScreenKind.External, 3840, 2160, 60, null, null, null, layout, subs);

            var d = DisplayCopyPlanner.Decide(Source, external, Target, true, false, FourK);

            Assert.True(d.Allowed);
            Assert.Equal("Copy RX1_c059e_SFPA - External layout (TX2_DUAL LINK_SFPA, VEGA_1100) - 3840x2160", d.Summary);
        }

        [Fact]
        public void NarrowerButTallerTarget_IsBlocked_BecauseWidthAndHeightAreComparedSeparately()
        {
            // 2560x1080 has more pixels (2,764,800) than 1920x1200 (2,304,000), yet it is too short.
            var ultrawide = EdidInfo.Parse(TestEdids.Build((2560, 1080, 60, false)))!;

            var d = DisplayCopyPlanner.Decide(Source, Single(1920, 1200), Target, true, false, ultrawide);

            Assert.False(d.Allowed);
            Assert.Equal("PAX355S_SFP A is 2560x1080 - RX1_c059e_SFPA's picture is 1920x1200. Copy needs a display at least that large.", d.Reason);
        }
    }
}
