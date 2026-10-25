using System;
using System.Linq;
using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class SurfacePlannerTests
    {
        private const string Rx = "74:FE:48:3C:05:9E";

        [Fact]
        public void Plan_DuplicateEncoder_SharesOneSurfaceAndOneSubscription()
        {
            var draft = new MultiViewDraft(MultiViewPresets.Build("matrix2x2", 1920, 1080));
            draft.Place(0, "00:11:22:33:44:02", "TX2");
            draft.Place(3, "001122334402", "TX2");

            var plan = SurfacePlanner.Plan(draft, Rx);

            Assert.NotNull(plan.Windows[0].Surface);
            Assert.Equal(plan.Windows[0].Surface, plan.Windows[3].Surface);
            var source = Assert.Single(plan.Sources);
            Assert.Equal("001122334402", source.Mac);
            Assert.Equal(plan.Windows[0].Surface, source.Surface);
            Assert.Equal((960, 540), (source.Width, source.Height));
        }

        [Fact]
        public void Plan_FromWindowGeometry_KeepsSurfacesOffsetsAndStreams_AndBlackensUnroutedWindows()
        {
            var layout = new AvasRoutingTool.Sdvoe.RxLayout("bavm_custom", 3840, 2160, new[]
            {
                new AvasRoutingTool.Sdvoe.RxWindow(1, 5, 1920, 0, 1920, 1080, 16, 8),
                new AvasRoutingTool.Sdvoe.RxWindow(0, 3, 0, 0, 1920, 1080),
                new AvasRoutingTool.Sdvoe.RxWindow(2, 3, 0, 1080, 1920, 1080),
                new AvasRoutingTool.Sdvoe.RxWindow(3, 255, 1920, 1080, 1920, 1080),
                new AvasRoutingTool.Sdvoe.RxWindow(4, 7, 960, 540, 960, 540),
            });
            var subs = new[]
            {
                new AvasRoutingTool.Sdvoe.RxSubscription(3, true, "STREAMING", "00:11:22:33:44:02", "TX2", 1),
                new AvasRoutingTool.Sdvoe.RxSubscription(5, true, "STREAMING", "001122334403", "TX3", 0),
                new AvasRoutingTool.Sdvoe.RxSubscription(7, false, "STOPPED", "001122334404", "TX4", 1), // not routed
                new AvasRoutingTool.Sdvoe.RxSubscription(9, true, "STREAMING", "001122334405", "TX5", 1), // no window shows it
            };

            var plan = SurfacePlanner.Plan(layout, subs, 3840, 2160, Rx);

            Assert.Equal("avas_74fe483c059e_copy_3840x2160", plan.LayoutName);
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, plan.Windows.Select(w => w.Slot.Index)); // window order
            Assert.Equal(new int?[] { 3, 5, 3, null, null }, plan.Windows.Select(w => w.Surface));
            var second = plan.Windows[1];
            Assert.Equal((1920, 0, 1920, 1080, 16, 8), (second.Slot.X, second.Slot.Y, second.Slot.Width, second.Slot.Height, second.HOffset, second.VOffset));
            Assert.Equal(new[] { ("001122334402", 3, 1), ("001122334403", 5, 0) }, plan.Sources.Select(s => (s.Mac, s.Surface, s.Stream)));
            Assert.Equal((1920, 1080), (plan.Sources[0].Width, plan.Sources[0].Height));
        }

        [Fact]
        public void Plan_DistinctEncoders_GetSurfacesOneUpwardsInBlockOrder()
        {
            var draft = new MultiViewDraft(MultiViewPresets.Build("matrix2x2", 1920, 1080));
            draft.Place(2, "tx3", "TX3");
            draft.Place(1, "tx2", "TX2");

            var plan = SurfacePlanner.Plan(draft, Rx);

            Assert.Equal(new[] { "tx2", "tx3" }, plan.Sources.Select(s => s.Mac));
            Assert.Equal(new[] { 1, 2 }, plan.Sources.Select(s => s.Surface));
        }

        [Fact]
        public void Plan_EmptyBlocks_AreBlack()
        {
            var draft = new MultiViewDraft(MultiViewPresets.Build("matrix2x2", 1920, 1080));
            draft.Place(1, "tx2", "TX2");

            var plan = SurfacePlanner.Plan(draft, Rx);

            Assert.Equal(4, plan.Windows.Count);
            Assert.Null(plan.Windows[0].Surface);
            Assert.NotNull(plan.Windows[1].Surface);
            Assert.Null(plan.Windows[2].Surface);
            Assert.Null(plan.Windows[3].Surface);
        }

        [Fact]
        public void Plan_NothingStaged_IsAllBlackWithNoSources()
        {
            var plan = SurfacePlanner.Plan(new MultiViewDraft(MultiViewPresets.Build("matrix2x2", 1920, 1080)), Rx);

            Assert.All(plan.Windows, w => Assert.Null(w.Surface));
            Assert.Empty(plan.Sources);
        }

        [Fact]
        public void Plan_OversizedBlockGetsSurfaceZero()
        {
            var layout = MultiViewPresets.Build("pip", 3840, 2160);
            var main = layout.Slots.Single(s => s.Label == "Main");
            var inset = layout.Slots.Single(s => s.Label == "Inset");
            var draft = new MultiViewDraft(layout);
            draft.Place(main.Index, "tx1", "TX1");
            draft.Place(inset.Index, "tx2", "TX2");

            var plan = SurfacePlanner.Plan(draft, Rx);

            var big = plan.Sources.Single(s => s.Mac == "tx1");
            Assert.Equal(0, big.Surface);
            Assert.Equal((3840, 2160), (big.Width, big.Height));
            Assert.Equal(1, plan.Sources.Single(s => s.Mac == "tx2").Surface);
        }

        [Fact]
        public void Plan_TwoDistinctOversizedEncoders_Throws()
        {
            var draft = new MultiViewDraft(Blocks(7680, 2160, (3840, 2160), (3840, 2160)));
            draft.Place(0, "tx1", "TX1");
            draft.Place(1, "tx2", "TX2");

            var ex = Assert.Throws<InvalidOperationException>(() => SurfacePlanner.Plan(draft, Rx));
            Assert.Contains("oversized", ex.Message);
        }

        [Fact]
        public void Plan_MoreThanElevenStandardEncoders_Throws()
        {
            var draft = new MultiViewDraft(Blocks(1920, 1080, Enumerable.Repeat((160, 90), 12).ToArray()));
            for (int i = 0; i < 12; i++) draft.Place(i, $"tx{i}", $"TX{i}");

            var ex = Assert.Throws<InvalidOperationException>(() => SurfacePlanner.Plan(draft, Rx));
            Assert.Contains("11", ex.Message);
        }

        [Fact]
        public void Plan_ElevenStandardEncoders_UseSurfacesOneToEleven()
        {
            var draft = new MultiViewDraft(Blocks(1920, 1080, Enumerable.Repeat((160, 90), 11).ToArray()));
            for (int i = 0; i < 11; i++) draft.Place(i, $"tx{i}", $"TX{i}");

            var plan = SurfacePlanner.Plan(draft, Rx);

            Assert.Equal(Enumerable.Range(1, 11), plan.Sources.Select(s => s.Surface));
        }

        /// <summary>A hand-made layout: blocks side by side at the given sizes (positions are irrelevant to planning).</summary>
        private static MultiViewLayoutPlan Blocks(int width, int height, params (int W, int H)[] sizes) =>
            new("custom", "custom", width, height,
                sizes.Select((s, i) => new WindowSlot(i, (i + 1).ToString(), 0, 0, s.W, s.H, 0, 0, 0, 1, 1)).ToList());

        [Fact]
        public void Plan_UsesThePerDisplayLayoutName()
        {
            var draft = new MultiViewDraft(MultiViewPresets.Build("matrix2x2", 1920, 1080));

            var plan = SurfacePlanner.Plan(draft, Rx);

            Assert.Equal("avas_74fe483c059e_matrix2x2_1920x1080", plan.LayoutName);
        }
    }
}
