using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>The read-only view state of <see cref="DisplayCardViewModel"/> and <see cref="MainViewModel"/> that replaces multi-flag XAML triggers (plan task A4).</summary>
    public class DisplayCardViewStateTests
    {
        private static DisplayCardViewModel Card(string activeSource = "") =>
            new(new AvasDevice { DeviceId = "74fe483c059e", DeviceName = "RX1", IsReceiver = true, IsActive = true, ActiveSourceId = activeSource, ActiveSourceName = activeSource });

        private static void ApplyMatrix(DisplayCardViewModel card) =>
            card.ApplyLayout(MultiViewPresets.Build("matrix2x2", 1920, 1080), "Matrix 2x2");

        [Fact]
        public void AnUnroutedCard_ShowsThePlaceholderOnly()
        {
            var card = Card();

            Assert.False(card.HasRoutedBadge);
            Assert.True(card.ShowSingleViewPicture);
            Assert.True(card.ShowUnroutedPlaceholder);
            Assert.False(card.ShowActiveSourceTag);
            Assert.False(card.ShowRoutedNonAvasNotice);
        }

        [Fact]
        public void ARoutedAvasCard_ShowsTheActiveSourceTag_AndTheRoutedBadge()
        {
            var card = Card("TX2");
            card.IsPreviewSupported = true;

            Assert.True(card.HasRoutedBadge);
            Assert.True(card.ShowActiveSourceTag);
            Assert.False(card.ShowUnroutedPlaceholder);
            Assert.False(card.ShowRoutedNonAvasNotice);
        }

        [Fact]
        public void ARoutedNonAvasCard_ShowsTheNoPreviewNotice_UnlessAnExternalLayoutRuns()
        {
            var card = Card("TX9");
            card.IsPreviewSupported = false;

            Assert.True(card.ShowRoutedNonAvasNotice);
            Assert.False(card.ShowActiveSourceTag);

            card.SetExternalLayout("avas_matrix", new[] { "TX1" }, "OUT 1920x1080@60");
            Assert.False(card.ShowRoutedNonAvasNotice);
            Assert.True(card.HasRoutedBadge);
        }

        [Fact]
        public void AMultiViewCard_HidesTheSingleViewPicture_AndKeepsTheBadge()
        {
            var card = Card();
            ApplyMatrix(card);

            Assert.True(card.IsMultiView);
            Assert.False(card.ShowSingleViewPicture);
            Assert.True(card.HasRoutedBadge);
        }

        [Fact]
        public void TheUndoToast_YieldsToTheDropChoiceAndTheCopyTargetStates()
        {
            var card = Card("TX1");
            Assert.False(card.ShowUndoToast);

            card.ShowUndoScreen("RX1 now shows TX2");
            Assert.True(card.ShowUndoToast);

            card.OpenDropChoice(new SourceCardViewModel(new AvasDevice { DeviceId = "tx", DeviceName = "TX", IsTransmitter = true }));
            Assert.False(card.ShowUndoToast);
            card.CloseDropChoice();
            Assert.True(card.ShowUndoToast);

            card.SetCopyTarget(new CopyDecision(true, "Release to copy", null, null));
            Assert.False(card.ShowUndoToast);
            card.SetCopyTarget(new CopyDecision(false, "Blocked", null, "no"));
            Assert.False(card.ShowUndoToast);
            card.SetCopyTarget(null);
            Assert.True(card.ShowUndoToast);

            card.ClearUndoScreen();
            Assert.False(card.ShowUndoToast);
        }

        [Fact]
        public void TheDraftNotice_HidesWhenThereIsNone_OrWhileThisDisplayIsTheCopySource()
        {
            var card = Card("TX1");
            Assert.False(card.ShowDraftNotice);

            card.DraftNotice = "Draft was made for the earlier screen";
            Assert.True(card.ShowDraftNotice);

            card.SetCopySource(true);
            Assert.False(card.ShowDraftNotice);
        }

        [Fact]
        public void PropertyChanged_FiresForTheDerivedState()
        {
            var card = Card();
            var changed = new List<string?>();
            card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            card.ShowUndoScreen("x");
            Assert.Contains(nameof(DisplayCardViewModel.ShowUndoToast), changed);

            changed.Clear();
            card.OpenDropChoice(new SourceCardViewModel(new AvasDevice { DeviceId = "tx", DeviceName = "TX", IsTransmitter = true }));
            Assert.Contains(nameof(DisplayCardViewModel.ShowUndoToast), changed);

            changed.Clear();
            ApplyMatrix(card);
            Assert.Contains(nameof(DisplayCardViewModel.ShowSingleViewPicture), changed);
            Assert.Contains(nameof(DisplayCardViewModel.HasRoutedBadge), changed);
            Assert.Contains(nameof(DisplayCardViewModel.ShowUnroutedPlaceholder), changed);

            changed.Clear();
            card.DraftNotice = "n";
            Assert.Contains(nameof(DisplayCardViewModel.ShowDraftNotice), changed);
        }

        [Fact]
        public void ALayoutTile_IsOnScreenOnlyWhileTheDisplayShowsIt()
        {
            var card = Card();
            Assert.All(card.LayoutMenu, t => Assert.False(t.IsOnScreen));   // an unrouted display marks no tile

            card.Device.ActiveSourceId = "tx1";
            card.Device.ActiveSourceName = "TX1";
            card.NotifyRoutingChanged();
            Assert.Equal(new[] { "single" }, card.LayoutMenu.Where(t => t.IsOnScreen).Select(t => t.Id));

            ApplyMatrix(card);
            Assert.Equal(new[] { "matrix2x2" }, card.LayoutMenu.Where(t => t.IsOnScreen).Select(t => t.Id));

            card.ClearLayout();
            card.Device.ActiveSourceId = string.Empty;
            card.NotifyRoutingChanged();
            Assert.All(card.LayoutMenu, t => Assert.False(t.IsOnScreen));
        }

        [Fact]
        public void ALayoutTile_RaisesPropertyChanged_WhenItGoesOnOrOffScreen()
        {
            var card = Card("TX1");
            var single = card.LayoutMenu.Single(t => t.Id == "single");
            var changed = new List<string?>();
            single.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            Assert.True(single.IsOnScreen);
            card.Device.ActiveSourceId = string.Empty;
            card.NotifyRoutingChanged();

            Assert.False(single.IsOnScreen);
            Assert.Contains(nameof(LayoutItemViewModel.IsOnScreen), changed);
        }

        [Fact]
        public async Task MainViewModel_IsComposing_FollowsTheOpenComposer()
        {
            var client = new MockSdvoeClient();
            var vm = new MainViewModel(new ConfigService(), client, new MockRtpReceiver(), new MulticastIpManager(),
                new RoutingOrchestrator(client), new MultiViewOrchestrator(client));
            var rx = new DisplayCardViewModel(new AvasDevice { DeviceId = "rx1", DeviceName = "RX1", IsReceiver = true, IsActive = true }, vm.UnrouteVideoAsync);
            vm.Displays.Add(rx);
            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            Assert.False(vm.IsComposing);

            await vm.OpenComposerAsync(rx, "matrix2x2");
            Assert.True(vm.IsComposing);
            Assert.Contains(nameof(MainViewModel.IsComposing), changed);

            changed.Clear();
            vm.CloseComposer();
            Assert.False(vm.IsComposing);
            Assert.Contains(nameof(MainViewModel.IsComposing), changed);
        }
    }
}
