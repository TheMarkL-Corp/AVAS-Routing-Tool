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
            Assert.Equal("Preview paused", avas.StreamStatusText);
            avas.IsStreaming = true;
            Assert.Equal("Waiting for signal", avas.StreamStatusText);
            avas.Fps = 29.97;
            Assert.Equal("30.0 FPS", avas.StreamStatusText);

            Assert.Equal("External source", Card(avasTransmitter: false).StreamStatusText);
        }

        [Fact]
        public void ShowConnecting_IsStreamingWithNoFrameYet()
        {
            var card = Card();
            Assert.False(card.ShowConnecting);
            card.IsStreaming = true;
            Assert.True(card.ShowConnecting);
            card.PreviewBitmap = new System.Windows.Media.Imaging.WriteableBitmap(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            Assert.False(card.ShowConnecting);
            card.IsStreaming = false;
            Assert.False(card.ShowConnecting);
            Assert.False(Card(avasTransmitter: false).ShowConnecting);
        }

        [Theory]
        [InlineData(0, false, "", false)]
        [InlineData(1, false, "in 1 block", true)]
        [InlineData(3, false, "in 3 blocks", true)]
        [InlineData(0, true, "", false)]
        [InlineData(2, true, "in 2 blocks", true)]
        public void UsageChips_FollowBlocksAndArming(int blocks, bool armed, string text, bool hasUsage)
        {
            var card = Card();
            card.UsedInBlocks = blocks;
            card.IsArmed = armed;

            Assert.Equal(text, card.UsedInText);
            Assert.Equal(hasUsage, card.HasUsage);
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
        }
    }
}
