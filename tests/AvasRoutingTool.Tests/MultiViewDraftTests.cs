using System.Linq;
using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class MultiViewDraftTests
    {
        private static MultiViewDraft DraftOf(string presetId) =>
            new(MultiViewPresets.Build(presetId, 1920, 1080));

        [Fact]
        public void Place_SameEncoderInTwoEqualBlocks_IsAllowed()
        {
            var draft = DraftOf("matrix2x2");

            Assert.True(draft.Place(0, "tx2", "TX2").Ok);
            var second = draft.Place(3, "tx2", "TX2");

            Assert.True(second.Ok);
            Assert.Equal(2, draft.Placements.Count);
            Assert.Equal(new[] { 0, 3 }, draft.SlotsShowing("tx2").ToArray());
        }

        [Fact]
        public void Place_NormalisesTheMacWhenLookingForRepeats()
        {
            var draft = DraftOf("matrix2x2");
            draft.Place(0, "00:11:22:33:44:55", "TX2");
            draft.Place(3, "001122334455", "TX2");

            Assert.Equal(new[] { 0, 3 }, draft.SlotsShowing("00-11-22-33-44-55").ToArray());
        }

        [Fact]
        public void Place_SameEncoderInADifferentSizeBlock_IsRefusedWithReason()
        {
            var draft = DraftOf("quad_1_3");
            Assert.True(draft.Place(0, "tx2", "TX2").Ok);

            var result = draft.Place(1, "tx2", "TX2");

            Assert.False(result.Ok);
            Assert.Equal("TX2 is in block 1 at 1280x720 - this block is 640x360. One encoder has one size per layout.", result.Reason);
            Assert.False(draft.Placements.ContainsKey(1));
        }

        [Fact]
        public void Place_ReplacingTheOnlyDifferentSizeUse_IsAllowed()
        {
            var draft = DraftOf("quad_1_3");
            draft.Place(0, "tx2", "TX2");
            Assert.True(draft.Place(0, "tx3", "TX3").Ok);

            Assert.True(draft.Place(1, "tx2", "TX2").Ok);
        }

        [Fact]
        public void Place_InABlockTheLayoutDoesNotHave_IsRefused()
        {
            var draft = DraftOf("side_by_side");

            Assert.False(draft.Place(5, "tx1", "TX1").Ok);
            Assert.Empty(draft.Placements);
        }

        [Fact]
        public void Move_ToEmptyBlock_Moves_ToOccupiedBlock_Swaps()
        {
            var draft = DraftOf("matrix2x2");
            draft.Place(0, "tx1", "TX1");
            draft.Place(1, "tx2", "TX2");

            Assert.True(draft.Move(0, 2).Ok);
            Assert.False(draft.Placements.ContainsKey(0));
            Assert.Equal("TX1", draft.Placements[2].SourceName);
            Assert.Equal(2, draft.Placements[2].SlotIndex);

            Assert.True(draft.Move(2, 1).Ok);
            Assert.Equal("TX2", draft.Placements[2].SourceName);
            Assert.Equal("TX1", draft.Placements[1].SourceName);
            Assert.Equal(1, draft.Placements[1].SlotIndex);
            Assert.Equal(2, draft.Placements.Count);
        }

        [Fact]
        public void Move_FromAnEmptyBlock_IsRefused()
        {
            var draft = DraftOf("matrix2x2");

            Assert.False(draft.Move(0, 1).Ok);
        }

        [Fact]
        public void Move_ThatWouldCreateASizeConflict_IsRefused()
        {
            var draft = DraftOf("quad_1_3");
            draft.Place(1, "tx2", "TX2");
            draft.Place(2, "tx2", "TX2");

            var result = draft.Move(1, 0);

            Assert.False(result.Ok);
            Assert.Equal("TX2 is in block 3 at 640x360 - this block is 1280x720. One encoder has one size per layout.", result.Reason);
            Assert.Equal(new[] { 1, 2 }, draft.SlotsShowing("tx2").ToArray());
        }

        [Fact]
        public void Clear_RemovesOnePlacement_ClearAll_RemovesEverything()
        {
            var draft = DraftOf("matrix2x2");
            draft.Place(0, "tx1", "TX1");
            draft.Place(1, "tx2", "TX2");

            draft.Clear(0);
            Assert.Equal(new[] { 1 }, draft.Placements.Keys.ToArray());

            draft.ClearAll();
            Assert.Empty(draft.Placements);
        }

        [Fact]
        public void WithLayout_RemapsByBlockOrder_AndListsWhatNoLongerFits()
        {
            var draft = DraftOf("matrix2x2");
            draft.Place(0, "tx1", "TX1");
            draft.Place(1, "tx2", "TX2");
            draft.Place(2, "tx3", "TX3");
            draft.Place(3, "tx4", "TX4");

            var next = draft.WithLayout(MultiViewPresets.Build("side_by_side", 1920, 1080));

            Assert.Equal("side_by_side", next.Plan.Id);
            Assert.Equal("TX1", next.Placements[0].SourceName);
            Assert.Equal("TX2", next.Placements[1].SourceName);
            Assert.Equal(2, next.Placements.Count);
            Assert.Equal(new[] { "TX3", "TX4" }, next.Unplaced.Select(p => p.SourceName).ToArray());
            Assert.Equal(4, draft.Placements.Count);
        }

        [Fact]
        public void WithLayout_DropsADuplicateThatBecomesASizeConflict()
        {
            var draft = DraftOf("matrix2x2");
            draft.Place(0, "tx2", "TX2");
            draft.Place(1, "tx2", "TX2");

            var next = draft.WithLayout(MultiViewPresets.Build("pip", 1920, 1080));

            Assert.Equal(new[] { 0 }, next.Placements.Keys.ToArray());
            Assert.Equal("TX2", Assert.Single(next.Unplaced).SourceName);
        }

        [Fact]
        public void DiffFrom_Live_ListsOnlyChangedBlocksInBlockOrder()
        {
            var live = DraftOf("matrix2x2");
            live.Place(0, "tx1", "TX1");
            live.Place(2, "tx3", "TX3");
            var draft = DraftOf("matrix2x2");
            draft.Place(0, "tx2", "TX2");
            draft.Place(1, "tx1", "TX1");
            draft.Place(2, "tx3", "TX3");

            var changes = draft.DiffFrom(live);

            Assert.Equal(new[]
            {
                new DraftChange(0, "TX1", "TX2"),
                new DraftChange(1, null, "TX1"),
            }, changes);
        }

        [Fact]
        public void DiffFrom_ABlockThatWasCleared_IsAChangeToEmpty()
        {
            var live = DraftOf("matrix2x2");
            live.Place(0, "tx1", "TX1");
            var draft = DraftOf("matrix2x2");

            Assert.Equal(new[] { new DraftChange(0, "TX1", null) }, draft.DiffFrom(live));
        }

        [Fact]
        public void DiffFrom_NullLive_TreatsEveryPlacementAsNew()
        {
            var draft = DraftOf("matrix2x2");
            draft.Place(1, "tx1", "TX1");
            draft.Place(0, "tx2", "TX2");

            Assert.Equal(new[]
            {
                new DraftChange(0, null, "TX2"),
                new DraftChange(1, null, "TX1"),
            }, draft.DiffFrom(null));
        }

        [Fact]
        public void Clone_IsIndependentOfTheOriginal()
        {
            var draft = DraftOf("matrix2x2");
            draft.Place(0, "tx1", "TX1");

            var copy = draft.Clone();
            copy.Place(1, "tx2", "TX2");
            copy.Clear(0);

            Assert.Equal(new[] { 0 }, draft.Placements.Keys.ToArray());
            Assert.Equal(new[] { 1 }, copy.Placements.Keys.ToArray());
        }
    }
}
