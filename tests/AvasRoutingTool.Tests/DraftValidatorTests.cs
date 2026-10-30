using System;
using System.Linq;
using AvasRoutingTool.Sdvoe.MultiView;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class DraftValidatorTests
    {
        private static readonly OtherDisplayUse[] NoOthers = Array.Empty<OtherDisplayUse>();

        private static MultiViewDraft DraftOf(string presetId) =>
            new(MultiViewPresets.Build(presetId, 1920, 1080));

        [Fact]
        public void Validate_OfflineEncoder_FlagsEveryBlockShowingIt()
        {
            var draft = DraftOf("matrix2x2");
            draft.Place(0, "tx2", "TX2");
            draft.Place(1, "tx1", "TX1");
            draft.Place(3, "tx2", "TX2");

            var issues = DraftValidator.Validate(draft, NoOthers, mac => mac != "tx2");

            Assert.Equal(new[] { 0, 3 }, issues.Select(i => i.SlotIndex).ToArray());
            Assert.All(issues, i =>
            {
                Assert.Equal(BlockIssueKind.SourceOffline, i.Kind);
                Assert.Equal("TX2 is offline", i.Message);
            });
        }

        [Fact]
        public void Validate_EncoderAtAnotherSizeOnAnotherDisplay_IsFlagged()
        {
            var draft = DraftOf("matrix2x2");
            draft.Place(0, "tx2", "TX2");
            var others = new[] { new OtherDisplayUse("RX2", "tx2", 1920, 1080) };

            var issue = Assert.Single(DraftValidator.Validate(draft, others, _ => true));

            Assert.Equal(0, issue.SlotIndex);
            Assert.Equal(BlockIssueKind.SizeConflictOtherDisplay, issue.Kind);
            Assert.Equal("TX2 runs at 1920x1080 on RX2 - this block is 960x540.", issue.Message);
        }

        [Fact]
        public void Validate_EncoderAtTheSameSizeOnAnotherDisplay_IsFine()
        {
            var draft = DraftOf("matrix2x2");
            draft.Place(0, "TX:2", "TX2");
            var others = new[] { new OtherDisplayUse("RX2", "tx2", 960, 540) };

            Assert.Empty(DraftValidator.Validate(draft, others, _ => true));
        }

        [Fact]
        public void Validate_PipInsetAtAnotherSizeOnAnotherDisplay_IsFine()
        {
            var draft = DraftOf("pip");
            draft.Place(0, "tx2", "TX2");
            var others = new[] { new OtherDisplayUse("RX1", "tx2", 1920, 1080) };

            Assert.Empty(DraftValidator.Validate(draft, others, _ => true));
        }

        [Fact]
        public void Validate_PipMainWindowAtAnotherSizeOnAnotherDisplay_IsStillFlagged()
        {
            var draft = DraftOf("pip");
            draft.Place(1, "tx2", "TX2");
            var others = new[] { new OtherDisplayUse("RX1", "tx2", 960, 540) };

            Assert.Single(DraftValidator.Validate(draft, others, _ => true));
        }

        [Fact]
        public void Validate_BlockAtAnotherSizeThanAnInsetOnAnotherDisplay_IsFine()
        {
            var draft = DraftOf("matrix2x2");
            draft.Place(0, "tx2", "TX2");
            var others = new[] { new OtherDisplayUse("RX2", "tx2", 480, 270, IsInset: true) };

            Assert.Empty(DraftValidator.Validate(draft, others, _ => true));
        }

        [Fact]
        public void CanPlace_PipInsetAtAnotherSize_IsAccepted()
        {
            var draft = DraftOf("pip");
            var others = new[] { new OtherDisplayUse("RX1", "tx2", 1920, 1080) };

            Assert.True(DraftValidator.CanPlace(draft, 0, "tx2", "TX2", others, _ => true).Ok);
        }

        [Fact]
        public void Validate_OtherDisplayMacsAreNormalised()
        {
            var draft = DraftOf("matrix2x2");
            draft.Place(0, "00:11:22:33:44:55", "TX2");
            var others = new[] { new OtherDisplayUse("RX2", "00-11-22-33-44-55", 1920, 1080) };

            Assert.Single(DraftValidator.Validate(draft, others, _ => true));
        }

        [Fact]
        public void Validate_AnEmptyDraft_HasNoIssues()
        {
            Assert.Empty(DraftValidator.Validate(DraftOf("pip"), NoOthers, _ => false));
        }

        [Fact]
        public void CanPlace_ReturnsTheSameReasonsBeforePlacing()
        {
            var offline = DraftValidator.CanPlace(DraftOf("matrix2x2"), 0, "tx2", "TX2", NoOthers, _ => false);
            Assert.False(offline.Ok);
            Assert.Equal("TX2 is offline", offline.Reason);

            var quad = DraftOf("quad_1_3");
            quad.Place(0, "tx2", "TX2");
            var sameLayout = DraftValidator.CanPlace(quad, 1, "tx2", "TX2", NoOthers, _ => true);
            Assert.False(sameLayout.Ok);
            Assert.Equal("TX2 is in block 1 at 1280x720 - this block is 640x360. One encoder has one size per layout.", sameLayout.Reason);

            var others = new[] { new OtherDisplayUse("RX2", "tx2", 1920, 1080) };
            var otherDisplay = DraftValidator.CanPlace(DraftOf("matrix2x2"), 0, "tx2", "TX2", others, _ => true);
            Assert.False(otherDisplay.Ok);
            Assert.Equal("TX2 runs at 1920x1080 on RX2 - this block is 960x540.", otherDisplay.Reason);

            Assert.True(DraftValidator.CanPlace(DraftOf("matrix2x2"), 0, "tx1", "TX1", others, _ => true).Ok);
        }

        [Fact]
        public void CanPlace_DoesNotChangeTheDraft()
        {
            var draft = DraftOf("matrix2x2");

            DraftValidator.CanPlace(draft, 0, "tx1", "TX1", NoOthers, _ => true);

            Assert.Empty(draft.Placements);
        }
    }
}
