using System.Collections.Generic;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>The read-only view state of <see cref="SourceCardViewModel"/> that replaces the multi-flag XAML triggers (plan task A3).</summary>
    public class SourceCardViewStateTests
    {
        private static SourceCardViewModel Card(bool avasTransmitter = true) =>
            new(new AvasDevice
            {
                DeviceId = "74fe488b07bb",
                DeviceName = "TX2",
                IsTransmitter = true,
                VendorId = avasTransmitter ? 105 : 7,
            });

        [Theory]
        [InlineData(true, false, true, false, true, true, false)]   // AVAS, idle: Refresh + Start + Standby
        [InlineData(true, true, true, true, false, false, false)]   // AVAS, streaming: Refresh + Stop
        public void StreamControls_FollowSupportAndStreaming(bool supported, bool streaming, bool refresh, bool stop, bool start, bool standby, bool unsupported)
        {
            var card = Card(supported);
            card.IsStreaming = streaming;

            Assert.Equal(refresh, card.ShowRefresh);
            Assert.Equal(stop, card.ShowStop);
            Assert.Equal(start, card.ShowStart);
            Assert.Equal(standby, card.ShowStandby);
            Assert.Equal(unsupported, card.ShowUnsupported);
        }

        [Fact]
        public void ANonAvasSource_ShowsOnlyTheUnsupportedNotice()
        {
            var card = Card(avasTransmitter: false);

            Assert.False(card.ShowRefresh);
            Assert.False(card.ShowStop);
            Assert.False(card.ShowStart);
            Assert.False(card.ShowStandby);
            Assert.True(card.ShowUnsupported);
        }

        [Fact]
        public void StreamStatusText_ReadsPausedExternalOrTheFrameRate()
        {
            var avas = Card();
            Assert.Equal("PREVIEW PAUSED", avas.StreamStatusText);
            avas.IsStreaming = true;
            avas.Fps = 29.97;
            Assert.Equal("30.0 FPS", avas.StreamStatusText);

            Assert.Equal("EXTERNAL SOURCE", Card(avasTransmitter: false).StreamStatusText);
        }

        [Theory]
        [InlineData(0, false, "", false, true)]
        [InlineData(1, false, "in 1 block", true, false)]
        [InlineData(3, false, "in 3 blocks", true, false)]
        [InlineData(0, true, "", false, false)]
        [InlineData(2, true, "in 2 blocks", true, false)]
        public void UsageChips_AndTheTapHint_FollowBlocksAndArming(int blocks, bool armed, string text, bool hasUsage, bool tapHint)
        {
            var card = Card();
            card.UsedInBlocks = blocks;
            card.IsArmed = armed;

            Assert.Equal(text, card.UsedInText);
            Assert.Equal(hasUsage, card.HasUsage);
            Assert.Equal(tapHint, card.ShowTapHint);
        }

        [Fact]
        public void PropertyChanged_FiresForTheDerivedState_WhenItsInputsChange()
        {
            var card = Card();
            var changed = new List<string?>();
            card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            card.IsStreaming = true;
            Assert.Contains(nameof(SourceCardViewModel.ShowStop), changed);
            Assert.Contains(nameof(SourceCardViewModel.ShowStart), changed);
            Assert.Contains(nameof(SourceCardViewModel.ShowStandby), changed);
            Assert.Contains(nameof(SourceCardViewModel.StreamStatusText), changed);

            changed.Clear();
            card.Fps = 30;
            Assert.Contains(nameof(SourceCardViewModel.StreamStatusText), changed);

            changed.Clear();
            card.UsedInBlocks = 2;
            Assert.Contains(nameof(SourceCardViewModel.UsedInText), changed);
            Assert.Contains(nameof(SourceCardViewModel.HasUsage), changed);
            Assert.Contains(nameof(SourceCardViewModel.ShowTapHint), changed);

            changed.Clear();
            card.IsArmed = true;
            Assert.Contains(nameof(SourceCardViewModel.ShowTapHint), changed);
        }
    }
}
