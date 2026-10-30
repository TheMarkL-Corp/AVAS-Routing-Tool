using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using AvasRoutingTool.ViewModels;
using Xunit;

// NOTE: test names in this file must not contain the substring "Live": the shared filter FullyQualifiedName!~Live
// (which keeps hardware tests out of the normal run) would silently skip them.
namespace AvasRoutingTool.Tests
{
    /// <summary>The Composer's state machine: arm, place, picker, move, Take, Undo. No device call before Take.</summary>
    public class MultiViewComposerViewModelTests
    {
        private sealed class Rig
        {
            public MockSdvoeClient Client { get; } = new();
            public MultiViewOrchestrator Mv { get; }
            public AvasDevice Rx { get; } = new() { DeviceId = "rx1", DeviceName = "RX1", IsReceiver = true, IsActive = true };
            public DisplayCardViewModel RxVm { get; }
            public SourceCardViewModel Tx1 { get; } = Source("tx1", "TX1");
            public SourceCardViewModel Tx2 { get; } = Source("tx2", "TX2");
            public SourceCardViewModel Tx3 { get; } = Source("tx3", "TX3");
            public List<SourceCardViewModel> Sources { get; }
            public List<LayoutItemViewModel> Layouts { get; } = new()
            {
                new LayoutItemViewModel { Id = "off", Title = "Off", IsAvailable = true },
                new LayoutItemViewModel { Id = "single", Title = "Single View", IsAvailable = true },
                new LayoutItemViewModel { Id = "matrix2x2", Title = "Matrix 2x2", IsAvailable = true },
                new LayoutItemViewModel { Id = "pip", Title = "PIP", IsAvailable = true },
                new LayoutItemViewModel { Id = "side_by_side", Title = "Side-by-Side", IsAvailable = true },
                new LayoutItemViewModel { Id = "quad_1_3", Title = "1+3 Quad", IsAvailable = true },
            };
            public DateTime Now { get; set; } = new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);

            public Rig()
            {
                Mv = new MultiViewOrchestrator(Client);
                RxVm = new DisplayCardViewModel(Rx);
                Sources = new List<SourceCardViewModel> { Tx1, Tx2, Tx3 };
            }

            public MultiViewComposerViewModel Composer(MultiViewDraft? resume = null) =>
                new(RxVm, Mv, () => Sources, Layouts, resume, () => Now);

            public async Task<MultiViewComposerViewModel> Matrix(MultiViewDraft? resume = null)
            {
                var vm = Composer(resume);
                await vm.SelectLayoutAsync("matrix2x2");
                return vm;
            }

            private static SourceCardViewModel Source(string mac, string name) =>
                new(new AvasDevice { DeviceId = mac, DeviceName = name, VendorId = 105, IsTransmitter = true, IsActive = true });
        }

        private static string[] Placed(MultiViewDraft? draft) =>
            draft == null
                ? Array.Empty<string>()
                : draft.Placements.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.SourceMac}").ToArray();

        // ---- screen reader text for the three steps and the ON SCREEN NOW picture ----

        [Fact]
        public async Task StepHeadings_SayTheirState_InWords()
        {
            var rig = new Rig();
            var vm = rig.Composer();
            Assert.Equal("Step 1, Layout: to do", vm.StepLayoutText);
            Assert.Equal("Step 2, Place sources: to do", vm.StepPlaceText);
            Assert.Equal("Step 3, Check, then take: not ready", vm.StepTakeText);

            await vm.SelectLayoutAsync("matrix2x2");
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);

            Assert.Equal("Step 1, Layout: done", vm.StepLayoutText);
            Assert.Equal("Step 2, Place sources: done", vm.StepPlaceText);
            Assert.Equal("Step 3, Check, then take: ready", vm.StepTakeText);
        }

        [Fact]
        public async Task StepHeadings_RaiseChange_WhenTheStateMoves()
        {
            var rig = new Rig();
            var vm = rig.Composer();
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            await vm.SelectLayoutAsync("matrix2x2");

            Assert.Contains(nameof(MultiViewComposerViewModel.StepLayoutText), raised);
            Assert.Contains(nameof(MultiViewComposerViewModel.StepPlaceText), raised);
            Assert.Contains(nameof(MultiViewComposerViewModel.StepTakeText), raised);
        }

        [Fact]
        public async Task OnScreenSummary_NamesWhatTheSurgeonSees()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            Assert.Equal("On screen now: no MultiView on this screen", vm.OnScreenSummary);

            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            await vm.TakeAsync();

            Assert.Equal("On screen now: 4 blocks. Block 1 TX1; block 2 empty; block 3 empty; block 4 empty", vm.OnScreenSummary);
        }

        [Fact]
        public async Task LiveAspectRatio_FollowsTheLivePlan_AndDefaultsTo16x9()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            Assert.Equal(16.0 / 9.0, vm.LiveAspectRatio, 5); // nothing live yet

            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            await vm.TakeAsync();
            var live = rig.Mv.GetLive(rig.Rx)!;
            Assert.Equal((double)live.Plan.Width / live.Plan.Height, vm.LiveAspectRatio, 5);

            // a 16:10 panel: the canvas the plan is built for is 1920x1200, and the mini picture must follow it
            var tall = new Rig();
            tall.Client.DisplayEdid = TestEdids.Build((1920, 1200, 60, false));
            var vm2 = await tall.Matrix();
            vm2.Arm(tall.Tx1);
            vm2.TapBlock(0);
            await vm2.TakeAsync();
            var live2 = tall.Mv.GetLive(tall.Rx)!;
            Assert.Equal((double)live2.Plan.Width / live2.Plan.Height, vm2.LiveAspectRatio, 5);
        }

        // ---- the 18 listed tests ----

        [Fact]
        public async Task Placing_RaisesChangeLines_SoTheChangeListFollows()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Arm(rig.Tx1);
            vm.TapBlock(0);

            Assert.Contains(nameof(MultiViewComposerViewModel.ChangeLines), raised);
            Assert.Equal(new[] { "1: - -> TX1" }, vm.ChangeLines);
        }

        [Fact]
        public async Task ChangeCount_FollowsTheChanges_AndIsRaised()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            Assert.Equal(0, vm.ChangeCount);
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            vm.TapBlock(2);

            Assert.Equal(2, vm.ChangeCount);
            Assert.Contains(nameof(MultiViewComposerViewModel.ChangeCount), raised);
        }

        [Fact]
        public async Task IsStaged_AndIsOnScreen_FollowTheScreen()
        {
            var rig = new Rig();
            var fresh = rig.Composer();
            Assert.False(fresh.IsStaged);
            Assert.False(fresh.IsOnScreen);

            var vm = await rig.Matrix();
            Assert.False(vm.IsStaged); // empty blocks, nothing on screen: nothing to take
            Assert.False(vm.IsOnScreen);

            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            Assert.True(vm.IsStaged);
            Assert.False(vm.IsOnScreen);
            Assert.Contains(nameof(MultiViewComposerViewModel.IsStaged), raised);
            Assert.Contains(nameof(MultiViewComposerViewModel.IsOnScreen), raised);

            vm.Disarm();
            await vm.TakeAsync();
            Assert.False(vm.IsStaged);
            Assert.True(vm.IsOnScreen);

            vm.Clear(0); // clearing a block that is on screen is a staged change
            Assert.True(vm.IsStaged);
            Assert.False(vm.IsOnScreen);
        }

        [Fact]
        public void NewComposer_IsAtStepOne_WithNothingStaged()
        {
            var rig = new Rig();

            var vm = rig.Composer();

            Assert.False(vm.StepLayoutDone);
            Assert.False(vm.StepPlaceDone);
            Assert.Empty(vm.Blocks);
            Assert.False(vm.CanTake);
            Assert.Equal("Choose a layout (Step 1).", vm.TakeBlockedReason);
            Assert.Equal(new[] { "matrix2x2", "pip", "side_by_side", "quad_1_3" }, vm.LayoutOptions.Select(l => l.Id));
        }

        [Fact]
        public async Task SelectLayout_ShowsEmptyBlocks_AtTheDisplaysOutput_NoDeviceChange()
        {
            var rig = new Rig();
            rig.Client.DisplayEdid = TestEdids.ViewSonic4k;
            var vm = rig.Composer();

            await vm.SelectLayoutAsync("matrix2x2");

            Assert.True(vm.StepLayoutDone);
            Assert.Equal("matrix2x2", vm.SelectedLayoutId);
            Assert.Equal("OUT 3840x2160@60 (VP3268-4K)", vm.OutputText);
            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.Equal(4, vm.Blocks.Count);
            Assert.All(vm.Blocks, b => Assert.Equal(BlockState.Empty, b.State));
            Assert.Equal(new[] { "1", "2", "3", "4" }, vm.Blocks.Select(b => b.Label));
            Assert.Equal("1920x1080", vm.Blocks[0].SizeText);
            Assert.Equal(0.5, vm.Blocks[1].NormalizedX, 3);
            Assert.Equal(0.5, vm.Blocks[1].NormalizedWidth, 3);
            Assert.Equal("Place at least one source (Step 2).", vm.TakeBlockedReason);
            Assert.Contains("Sources at 30 fps", vm.RateNote); // four 1080p60 windows exceed the guideline
        }

        [Fact]
        public async Task Armed_TapTwoEqualBlocks_PlacesTheEncoderInBoth_AndStaysArmed()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();

            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            vm.TapBlock(2);

            Assert.Same(rig.Tx1, vm.ArmedSource);
            Assert.Equal(new[] { "0:tx1", "2:tx1" }, Placed(vm.Draft));
            Assert.Equal("Same as 1", vm.Blocks[2].SameAsText);
            Assert.Equal(string.Empty, vm.Blocks[0].SameAsText);
            Assert.Equal("TX1", vm.Blocks[2].SourceName);
            Assert.Empty(rig.Client.MultiViewCalls);
        }

        [Fact]
        public async Task Armed_DifferentSizeBlock_IsUnavailableWithReason()
        {
            var rig = new Rig();
            var vm = rig.Composer();
            await vm.SelectLayoutAsync("quad_1_3"); // block 1 is large, blocks 2-4 are small
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);

            Assert.True(vm.Blocks[0].IsAvailableForArmed);
            Assert.False(vm.Blocks[1].IsAvailableForArmed);
            string big = vm.Blocks[0].SizeText;
            string small = vm.Blocks[1].SizeText;
            Assert.Equal($"TX1 is in block 1 at {big} - this block is {small}. One encoder has one size per layout.",
                vm.Blocks[1].UnavailableReason);

            vm.TapBlock(1); // refused: the draft is unchanged and the operator is told why
            Assert.Equal(new[] { "0:tx1" }, Placed(vm.Draft));
            Assert.Equal(BannerKind.Warning, vm.Banner.Kind);
            Assert.Contains("One encoder has one size per layout", vm.Banner.Text);
        }

        [Fact]
        public async Task PlacedBlock_MirrorsTheEncodersPreview()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            Assert.Null(vm.Blocks[0].PreviewBitmap);

            var bitmap = new WriteableBitmap(16, 16, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null);
            rig.Tx1.PreviewBitmap = bitmap;

            Assert.Same(bitmap, vm.Blocks[0].PreviewBitmap);
            Assert.True(vm.Blocks[0].IsPreviewSupported);

            var later = new WriteableBitmap(16, 16, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null);
            rig.Tx1.PreviewBitmap = later;
            Assert.Same(later, vm.Blocks[0].PreviewBitmap);

            vm.Clear(0);
            Assert.Null(vm.Blocks[0].PreviewBitmap);
        }

        [Fact]
        public async Task TapEmptyBlock_Unarmed_OpensThePicker_PickPlacesAndCloses()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();

            vm.TapBlock(1);
            Assert.Equal(1, vm.PickerBlockIndex);
            Assert.True(vm.CheckPlace(1, rig.Tx2).Ok);

            vm.Pick(1, rig.Tx2);

            Assert.Null(vm.PickerBlockIndex);
            Assert.Equal(new[] { "1:tx2" }, Placed(vm.Draft));
            Assert.Null(vm.ArmedSource);
        }

        [Fact]
        public async Task Move_ThenTapAnotherBlock_SwapsThem()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            vm.Arm(rig.Tx2);
            vm.TapBlock(1);
            vm.Disarm();

            vm.BeginMove(0);
            Assert.Equal(0, vm.MovingFromIndex);
            vm.TapBlock(1);

            Assert.Null(vm.MovingFromIndex);
            Assert.Equal(new[] { "0:tx2", "1:tx1" }, Placed(vm.Draft));

            vm.BeginMove(1); // onto an empty block: a plain move
            vm.TapBlock(3);
            Assert.Equal(new[] { "0:tx2", "3:tx1" }, Placed(vm.Draft));
        }

        [Fact]
        public async Task CornerClear_RemovesThePlacementInOneTap_AndNothingReachesTheDevice()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            vm.Disarm();
            var block = vm.Blocks[0];
            Assert.True(block.ClearCommand.CanExecute(null));
            Assert.False(vm.Blocks[1].ClearCommand.CanExecute(null)); // nothing to clear

            block.ClearCommand.Execute(null);

            Assert.Empty(vm.Draft!.Placements);
            Assert.Equal(BlockState.Empty, block.State);
            Assert.Equal(string.Empty, block.SourceName);
            Assert.Empty(rig.Client.MultiViewCalls);
        }

        [Fact]
        public async Task TapPlacedBlock_OpensTheReplaceMoveStrip()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            vm.Disarm();

            vm.TapBlock(0);
            Assert.True(vm.Blocks[0].IsSelected);
            Assert.Null(vm.PickerBlockIndex);

            vm.TapBlock(0); // a second tap closes it
            Assert.False(vm.Blocks[0].IsSelected);
        }

        [Fact]
        public async Task TakeLabel_CountsChanges_AndCanTakeFollowsIssues()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            Assert.Equal("TAKE 1 CHANGE TO SCREEN", vm.TakeLabel);
            vm.Arm(rig.Tx2);
            vm.TapBlock(1);
            vm.Disarm();

            Assert.Equal("TAKE 2 CHANGES TO SCREEN", vm.TakeLabel);
            Assert.Equal(2, vm.Changes.Count);
            Assert.True(vm.CanTake);
            Assert.Null(vm.TakeBlockedReason);

            rig.Tx2.SetOnlineState(false); // the encoder drops off the network

            Assert.False(vm.CanTake);
            Assert.Equal("TX2 is offline - clear or replace block 2", vm.TakeBlockedReason);
            Assert.Equal(BlockState.Issue, vm.Blocks[1].State);
            Assert.Equal("TX2 is offline", vm.Blocks[1].IssueText);

            vm.Clear(1);
            Assert.True(vm.CanTake);
        }

        [Fact]
        public async Task Take_WithNothingPlaced_IsBlocked()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();

            Assert.False(vm.CanTake);
            Assert.Equal("Place at least one source (Step 2).", vm.TakeBlockedReason);

            await vm.TakeAsync();

            Assert.Empty(rig.Client.MultiViewCalls);
        }

        [Fact]
        public async Task Take_EmptyBlocks_AreAllowed_AndGoOnScreenBlack()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            Assert.True(vm.CanTake);

            await vm.TakeAsync();

            Assert.All(vm.Blocks, b => Assert.Equal(BlockState.Live, b.State));
            Assert.All(vm.Blocks.Skip(1), b => Assert.Equal(string.Empty, b.SourceName));
            Assert.Equal(4, rig.Client.LastSurfacePlan!.Windows.Count);
            Assert.Equal(new int?[] { 1, null, null, null }, rig.Client.LastSurfacePlan.Windows.Select(w => w.Surface));
        }

        [Fact]
        public async Task Take_Success_PutsTheStagedPictureOnScreen_AndOffersUndo()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            Assert.Equal(BlockState.Staged, vm.Blocks[0].State);
            Assert.Empty(vm.LiveBlocks);
            var raised = new List<MultiViewDraft?>();
            vm.LiveChanged += raised.Add;

            await vm.TakeAsync();

            Assert.Equal(BlockState.Live, vm.Blocks[0].State);
            Assert.Equal("On screen", vm.Banner.Text);
            Assert.Equal(BannerKind.Success, vm.Banner.Kind);
            Assert.Equal(4, vm.LiveBlocks.Count);
            Assert.Equal("TX1", vm.LiveBlocks[0].SourceName);
            Assert.Equal(new[] { "0:tx1" }, Placed(rig.Mv.GetLive(rig.Rx)));
            Assert.Empty(vm.Changes);
            Assert.False(vm.CanTake);
            Assert.Equal("Nothing to change since what is on screen.", vm.TakeBlockedReason);
            Assert.Single(raised);
            Assert.False(vm.IsBusy);

            // The orchestrator offers Undo only with something to go back to: a second Take.
            vm.Arm(rig.Tx2);
            vm.TapBlock(1);
            await vm.TakeAsync();
            Assert.True(vm.CanUndo);
        }

        [Fact]
        public async Task Undo_IsOfferedFor10Seconds_ThenWithdrawn()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            await vm.TakeAsync();
            vm.Arm(rig.Tx2);
            vm.TapBlock(1);
            await vm.TakeAsync();
            var seen = new List<string?>();
            vm.PropertyChanged += (_, e) => seen.Add(e.PropertyName);
            var takenAt = rig.Now;

            rig.Now = takenAt.AddSeconds(9.9);
            vm.Tick();
            Assert.True(vm.CanUndo);

            seen.Clear();
            rig.Now = takenAt.AddSeconds(10);
            vm.Tick();
            Assert.False(vm.CanUndo);
            Assert.Contains(nameof(MultiViewComposerViewModel.CanUndo), seen);

            await vm.UndoAsync(); // withdrawn: nothing happens
            Assert.Equal(new[] { "0:tx1", "1:tx2" }, Placed(rig.Mv.GetLive(rig.Rx)));
        }

        [Fact]
        public async Task Undo_WithinTheWindow_PutsThePreviousPictureBack()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            await vm.TakeAsync();
            vm.Arm(rig.Tx2);
            vm.TapBlock(1);
            await vm.TakeAsync();

            await vm.UndoAsync();

            Assert.Equal(new[] { "0:tx1" }, Placed(rig.Mv.GetLive(rig.Rx)));
            Assert.Equal(new[] { "0:tx1" }, Placed(vm.Draft));
            Assert.False(vm.CanUndo);
            Assert.Equal(BannerKind.Success, vm.Banner.Kind);
            Assert.Equal("TX1", vm.LiveBlocks[0].SourceName);
            Assert.Equal(string.Empty, vm.LiveBlocks[1].SourceName);
        }

        [Fact]
        public async Task Take_Rejected_KeepsTheDraft_AndSaysNothingChanged()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            rig.Client.FailMultiViewPrefix = "multiview:";

            await vm.TakeAsync();

            Assert.Contains("Nothing changed on screen", vm.Banner.Text);
            Assert.Contains("RX1", vm.Banner.Text); // the hardware's reason follows
            Assert.Equal(BannerKind.Error, vm.Banner.Kind);
            Assert.Equal(new[] { "0:tx1" }, Placed(vm.Draft));
            Assert.Equal(BlockState.Staged, vm.Blocks[0].State);
            Assert.True(vm.CanTake);
            Assert.False(vm.IsBusy);
            Assert.Empty(vm.LiveBlocks);
        }

        [Fact]
        public async Task Take_DisplayChanged_RedrawsTheLayoutAndAsksToReview()
        {
            var rig = new Rig();
            rig.Client.DisplayEdid = TestEdids.Build((1920, 1080, 60, false));
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            vm.TapBlock(2);
            Assert.Equal("960x540", vm.Blocks[0].SizeText);

            rig.Client.DisplayEdid = TestEdids.ViewSonic4k; // a 4K panel was plugged in meanwhile
            await vm.TakeAsync();

            // The orchestrator's own reason is shown (it names the display and the new format).
            Assert.StartsWith("The display of 'RX1' changed to", vm.Banner.Text);
            Assert.Contains("the layout was redrawn for it", vm.Banner.Text);
            Assert.Equal(BannerKind.Warning, vm.Banner.Kind);
            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.Equal(3840, vm.Draft!.Plan.Width);
            Assert.Equal("1920x1080", vm.Blocks[0].SizeText);
            Assert.Equal(new[] { "0:tx1", "2:tx1" }, Placed(vm.Draft)); // the sources stay where they were
            Assert.Equal("OUT 3840x2160@60 (VP3268-4K)", vm.OutputText);
            Assert.True(vm.CanTake);
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async Task SuccessBanner_ExpiresAfterFourSeconds_OnTick()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            await vm.TakeAsync();
            Assert.Equal(BannerKind.Success, vm.Banner.Kind);

            rig.Now += TimeSpan.FromSeconds(3.9);
            vm.Tick();
            Assert.Equal(BannerKind.Success, vm.Banner.Kind);

            rig.Now += TimeSpan.FromSeconds(0.1);
            vm.Tick();
            Assert.Equal(BannerKind.None, vm.Banner.Kind);
            Assert.Equal(string.Empty, vm.Banner.Text);
        }

        [Fact]
        public async Task SuccessBanner_ExpiryDoesNotTouchAnotherBanner()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            await vm.TakeAsync();
            vm.Arm(rig.Tx2); // the "Placing..." hint replaces the success line
            Assert.Equal(BannerKind.Info, vm.Banner.Kind);

            rig.Now += TimeSpan.FromSeconds(5);
            vm.Tick();

            Assert.Equal(BannerKind.Info, vm.Banner.Kind);
        }

        [Fact]
        public async Task SuccessBanner_ClearsOnTheFirstDraftEdit()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            await vm.TakeAsync();
            Assert.Equal(BannerKind.Success, vm.Banner.Kind);

            vm.Clear(0);
            Assert.Equal(BannerKind.None, vm.Banner.Kind);
        }

        [Fact]
        public async Task SuccessBanner_ClearsWhenASourceIsPickedAfterIt()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            await vm.TakeAsync();
            Assert.Equal(BannerKind.Success, vm.Banner.Kind);

            vm.OpenPicker(1);
            vm.Pick(1, rig.Tx2);

            Assert.Equal(BannerKind.None, vm.Banner.Kind);
        }

        [Fact]
        public async Task Take_WhileBusy_IsIgnored()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            var gate = new TaskCompletionSource();
            rig.Client.DuringCall = entry => entry.StartsWith("multiview:") ? gate.Task : Task.CompletedTask;

            var first = vm.TakeAsync();
            Assert.True(vm.IsBusy);
            Assert.False(vm.CanTake);
            Assert.Equal("Sending to RX1...", vm.TakeLabel);
            var second = vm.TakeAsync();
            vm.TapBlock(1); // the Composer is locked while sending
            vm.Clear(0);
            gate.SetResult();
            await Task.WhenAll(first, second);

            Assert.Single(rig.Client.MultiViewCalls, c => c.StartsWith("multiview:"));
            Assert.Equal(new[] { "0:tx1" }, Placed(vm.Draft));
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async Task Discard_ReturnsToWhatIsOnScreen()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            await vm.TakeAsync();
            vm.Arm(rig.Tx2);
            vm.TapBlock(1);
            vm.Clear(0);
            Assert.Equal(2, vm.Changes.Count);

            vm.Discard();

            Assert.Equal(new[] { "0:tx1" }, Placed(vm.Draft));
            Assert.Empty(vm.Changes);
            Assert.Equal(BlockState.Live, vm.Blocks[0].State);
            Assert.Null(vm.ArmedSource);
        }

        [Fact]
        public async Task Discard_WithNothingOnScreen_EmptiesTheDraftOnTheCurrentLayout()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);

            vm.Discard();

            Assert.Empty(vm.Draft!.Placements);
            Assert.Equal("matrix2x2", vm.Draft.Plan.Id);
            Assert.True(vm.StepLayoutDone);
            Assert.Equal(4, vm.Blocks.Count);
        }

        [Fact]
        public async Task ChangeLayout_KeepsSourcesByBlockOrder_AndListsUnplaced()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            vm.Arm(rig.Tx2);
            vm.TapBlock(1);
            vm.Arm(rig.Tx3);
            vm.TapBlock(3);
            vm.Disarm();

            await vm.SelectLayoutAsync("side_by_side"); // two blocks

            Assert.Equal(new[] { "0:tx1", "1:tx2" }, Placed(vm.Draft));
            Assert.Equal(new[] { "tx3" }, vm.UnplacedSources.Select(u => u.SourceMac));
            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.Equal(BannerKind.Info, vm.Banner.Kind);

            // Placing the leftover again takes it off the Unplaced list.
            vm.Clear(1);
            vm.ArmUnplaced(vm.UnplacedSources[0]);
            Assert.Same(rig.Tx3, vm.ArmedSource);
            vm.TapBlock(1);
            Assert.Empty(vm.UnplacedSources);
        }

        // ---- decisions from the controller context ----

        [Fact]
        public async Task ChangeLayout_AloneIsATakeableChange()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            await vm.TakeAsync();
            Assert.False(vm.CanTake);

            await vm.SelectLayoutAsync("side_by_side"); // same sources, new layout

            Assert.Empty(vm.Changes);
            Assert.True(vm.CanTake);
            Assert.Equal("TAKE NEW LAYOUT TO SCREEN", vm.TakeLabel);
        }

        [Fact]
        public async Task Resume_StartsStagedWithTheGivenDraft()
        {
            var rig = new Rig();
            var (plan, _) = await rig.Mv.PlanForDisplayAsync(rig.Rx, "pip");
            var draft = new MultiViewDraft(plan);
            Assert.True(draft.Place(1, "tx2", "TX2").Ok);

            var vm = rig.Composer(draft);

            Assert.True(vm.StepLayoutDone);
            Assert.True(vm.StepPlaceDone);
            Assert.Equal("pip", vm.SelectedLayoutId);
            Assert.Equal(2, vm.Blocks.Count);
            Assert.Equal("TX2", vm.Blocks[1].SourceName);
            Assert.StartsWith("OUT 1920x1080", vm.OutputText);
            Assert.Contains("RX1", vm.HeaderNotice);
            Assert.True(vm.CanTake);
        }

        [Fact]
        public async Task ChangedElsewhere_UpdatesOnScreenNow_KeepsTheDraft_AndSaysSo()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            var (plan, _) = await rig.Mv.PlanForDisplayAsync(rig.Rx, "matrix2x2");
            var elsewhere = new MultiViewDraft(plan);
            Assert.True(elsewhere.Place(1, "tx2", "TX2").Ok);

            vm.OnLiveChangedElsewhere(elsewhere);

            Assert.Equal("TX2", vm.LiveBlocks[1].SourceName);
            Assert.Equal(new[] { "0:tx1" }, Placed(vm.Draft));
            Assert.Contains("was changed elsewhere - your draft is kept", vm.Banner.Text);
            Assert.Equal(BannerKind.Warning, vm.Banner.Kind);
            Assert.Equal(2, vm.Changes.Count); // recomputed against the new live picture
        }

        [Fact]
        public async Task ChangedElsewhere_WhenTheDraftHasNoChanges_StagingFollowsTheScreen()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            await vm.TakeAsync();
            var (plan, _) = await rig.Mv.PlanForDisplayAsync(rig.Rx, "matrix2x2");
            var elsewhere = new MultiViewDraft(plan);
            Assert.True(elsewhere.Place(0, "tx1", "TX1").Ok);

            vm.OnLiveChangedElsewhere(elsewhere);

            Assert.Empty(vm.Changes);
            Assert.Equal(new[] { "0:tx1" }, Placed(vm.Draft));
            Assert.DoesNotContain("draft is kept", vm.Banner.Text); // nothing of the operator's is at stake
            Assert.Equal(BlockState.Live, vm.Blocks[0].State);
        }

        [Fact]
        public async Task Unplaced_ListsOneEntryPerSource()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(2);
            vm.TapBlock(3); // the same encoder in two blocks that will both spill
            vm.Disarm();

            await vm.SelectLayoutAsync("side_by_side");

            Assert.Single(vm.UnplacedSources);
        }

        [Fact]
        public async Task ClearAll_ClearsPlacementsOnly()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            vm.TapBlock(2);

            vm.ClearAll();

            Assert.Empty(vm.Draft!.Placements);
            Assert.Equal(4, vm.Blocks.Count);
            Assert.Same(rig.Tx1, vm.ArmedSource);
        }

        [Fact]
        public async Task Arm_TwiceDisarms_AndArmingClosesThePickerAndMove()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.TapBlock(1);
            Assert.Equal(1, vm.PickerBlockIndex);

            vm.Arm(rig.Tx1);
            Assert.Null(vm.PickerBlockIndex);
            Assert.Same(rig.Tx1, vm.ArmedSource);
            Assert.Contains("Placing TX1", vm.Banner.Text);

            vm.Arm(rig.Tx1);
            Assert.Null(vm.ArmedSource);
        }

        [Fact]
        public async Task Armed_BlocksOfAnOfflineEncoder_AreAllUnavailable()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            rig.Tx2.SetOnlineState(false);

            vm.Arm(rig.Tx2);

            Assert.All(vm.Blocks, b =>
            {
                Assert.False(b.IsAvailableForArmed);
                Assert.Equal("TX2 is offline", b.UnavailableReason);
            });
        }

        // ---- fix round 1 ----

        [Fact]
        public async Task OverlappingSelects_TheSecondTapWins_WhenItFinishesFirst()
        {
            var rig = new Rig();
            var gates = new List<TaskCompletionSource>();
            rig.Client.DuringEdidRead = () => { var g = new TaskCompletionSource(); gates.Add(g); return g.Task; };
            var vm = rig.Composer();

            var first = vm.SelectLayoutAsync("matrix2x2");
            var second = vm.SelectLayoutAsync("pip");
            Assert.Equal(2, gates.Count);
            Assert.True(vm.IsSelecting);
            Assert.Equal("pip", vm.PendingLayoutId);
            Assert.Null(vm.SelectedLayoutId);

            gates[1].SetResult();
            await second;
            Assert.Equal("pip", vm.SelectedLayoutId);
            Assert.False(vm.IsSelecting);

            gates[0].SetResult(); // the stale answer arrives late and is dropped
            await first;
            Assert.Equal("pip", vm.SelectedLayoutId);
            Assert.Equal("pip", vm.Draft!.Plan.Id);
            Assert.Equal(2, vm.Blocks.Count);
        }

        [Fact]
        public async Task OverlappingSelects_TheSecondTapWins_WhenTheFirstFinishesFirst()
        {
            var rig = new Rig();
            var gates = new List<TaskCompletionSource>();
            rig.Client.DuringEdidRead = () => { var g = new TaskCompletionSource(); gates.Add(g); return g.Task; };
            var vm = rig.Composer();

            var first = vm.SelectLayoutAsync("matrix2x2");
            var second = vm.SelectLayoutAsync("pip");

            gates[0].SetResult();
            await first;
            Assert.Null(vm.Draft); // the stale layout was not adopted
            Assert.True(vm.IsSelecting);

            gates[1].SetResult();
            await second;
            Assert.Equal("pip", vm.SelectedLayoutId);
            Assert.Equal("pip", vm.Draft!.Plan.Id);
            Assert.False(vm.IsSelecting);
            Assert.Null(vm.PendingLayoutId);
        }

        [Fact]
        public async Task Take_DuringAnInFlightSelect_DoesNothing()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            var gate = new TaskCompletionSource();
            rig.Client.DuringEdidRead = () => gate.Task;

            var select = vm.SelectLayoutAsync("pip");
            Assert.False(vm.CanTake);
            await vm.TakeAsync();
            await vm.UndoAsync();

            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.Equal("matrix2x2", vm.Draft!.Plan.Id); // not swapped under the Take
            gate.SetResult();
            await select;
            Assert.Equal("pip", vm.Draft!.Plan.Id);
            Assert.True(vm.CanTake);
        }

        [Fact]
        public async Task LayoutOptions_AreOwnedByTheComposer_AndFollowSelectedAndPending()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            var matrix = vm.LayoutOptions.Single(o => o.Id == "matrix2x2");
            var pip = vm.LayoutOptions.Single(o => o.Id == "pip");

            Assert.Equal("Matrix 2x2", matrix.Title);
            Assert.True(matrix.IsSelected);
            Assert.False(pip.IsSelected);
            Assert.All(rig.Layouts, l => Assert.False(l.IsSelected)); // the overview's items are not touched

            var gate = new TaskCompletionSource();
            rig.Client.DuringEdidRead = () => gate.Task;
            var select = vm.SelectLayoutAsync("pip");
            Assert.True(pip.IsPending);
            Assert.True(matrix.IsSelected);

            gate.SetResult();
            await select;
            Assert.True(pip.IsSelected);
            Assert.False(pip.IsPending);
            Assert.False(matrix.IsSelected);
            Assert.All(rig.Layouts, l => Assert.False(l.IsSelected));
        }

        [Fact]
        public async Task Resumed_Take_RefreshesTheOutputFormat_AndTheRateNote()
        {
            var rig = new Rig();
            rig.Client.DisplayEdid = TestEdids.ViewSonic4k;
            var (plan, _) = await rig.Mv.PlanForDisplayAsync(rig.Rx, "matrix2x2");
            var draft = new MultiViewDraft(plan);
            Assert.True(draft.Place(0, "tx1", "TX1").Ok);
            var vm = rig.Composer(draft);
            Assert.Equal("OUT 3840x2160", vm.OutputText); // nothing live yet: only the plan's size is known

            await vm.TakeAsync();

            Assert.Equal("OUT 3840x2160@60 (VP3268-4K)", vm.OutputText);
            Assert.Contains("30 fps", vm.RateNote);
        }

        [Fact]
        public async Task ChangedElsewhere_ForTheComposersOwnTake_IsIdempotent()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            await vm.TakeAsync();
            vm.Arm(rig.Tx2);
            vm.TapBlock(1);
            await vm.TakeAsync();
            Assert.True(vm.CanUndo);
            var banner = vm.Banner;

            vm.OnLiveChangedElsewhere(rig.Mv.GetLive(rig.Rx)); // the overview re-announces what we just took

            Assert.True(vm.CanUndo);
            Assert.Equal(banner, vm.Banner);
        }

        [Fact]
        public async Task ChangedElsewhere_ToNothing_EmptiesOnScreenNow_AndKeepsTheDraft()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            await vm.TakeAsync();
            Assert.Equal(4, vm.LiveBlocks.Count);

            vm.OnLiveChangedElsewhere(null); // e.g. someone turned the display off

            Assert.Empty(vm.LiveBlocks);
            Assert.False(vm.CanUndo);
            Assert.Equal(new[] { "0:tx1" }, Placed(vm.Draft));
            Assert.Single(vm.Changes);
            Assert.Equal(BlockState.Staged, vm.Blocks[0].State);
            Assert.Equal(BannerKind.Warning, vm.Banner.Kind);
            Assert.Contains("was changed elsewhere", vm.Banner.Text);
        }

        [Fact]
        public async Task Pip_NumbersAreDisplayNumbers_WhileCommandsTakeTheApiIndex()
        {
            var rig = new Rig();
            var vm = rig.Composer();
            await vm.SelectLayoutAsync("pip");
            Assert.Equal(new[] { 0, 1 }, vm.Blocks.Select(b => b.Index));
            Assert.Equal(new[] { "1", "2" }, vm.Blocks.Select(b => b.Label));

            vm.Arm(rig.Tx1);
            vm.TapBlock(0); // API index 0 = the inset = block 1
            vm.Disarm();
            rig.Tx1.SetOnlineState(false);
            Assert.Equal("TX1 is offline - clear or replace block 1", vm.TakeBlockedReason);

            rig.Tx1.SetOnlineState(true);
            vm.BeginMove(0);
            vm.TapBlock(1); // to the background = block 2

            Assert.Equal(new[] { "1:tx1" }, Placed(vm.Draft));
            Assert.Equal(1, vm.Changes[0].SlotIndex); // raw change keeps the API index
            Assert.Equal(new[] { "2: - -> TX1" }, vm.ChangeLines); // the list shows the display number
            rig.Tx1.SetOnlineState(false);
            Assert.Equal("TX1 is offline - clear or replace block 2", vm.TakeBlockedReason);
        }

        [Fact]
        public async Task Quad_SameAs_NamesTheDisplayNumberOfTheFirstBlock()
        {
            var rig = new Rig();
            var vm = rig.Composer();
            await vm.SelectLayoutAsync("quad_1_3");

            vm.Arm(rig.Tx1);
            vm.TapBlock(2); // block 3
            vm.TapBlock(3); // block 4, same size

            Assert.Equal(string.Empty, vm.Blocks[2].SameAsText);
            Assert.Equal("Same as 3", vm.Blocks[3].SameAsText);
        }

        [Fact]
        public async Task AfterDispose_CardChangesAndRefreshDoNothing()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            vm.Dispose();
            int raised = 0;
            vm.PropertyChanged += (_, _) => raised++;

            rig.Tx1.SetOnlineState(false);
            vm.Refresh();
            rig.Tx1.SetOnlineState(true); // would re-validate if Refresh had re-subscribed

            Assert.Equal(0, raised);
        }

        [Fact]
        public async Task CardsRemovedFromTheSources_AreNoLongerWatched()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            rig.Sources.Remove(rig.Tx3);
            vm.Refresh();
            int raised = 0;
            vm.PropertyChanged += (_, _) => raised++;

            rig.Tx3.SetOnlineState(false);

            Assert.Equal(0, raised);
        }

        [Fact]
        public async Task Undo_OnAChangedDisplay_SaysSo_AndChangesNothing()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            await vm.TakeAsync();
            vm.Arm(rig.Tx2);
            vm.TapBlock(1);
            await vm.TakeAsync();
            rig.Client.MultiViewCalls.Clear();

            rig.Client.DisplayEdid = TestEdids.ViewSonic4k; // a 4K panel was plugged in meanwhile
            await vm.UndoAsync();

            Assert.Equal(BannerKind.Warning, vm.Banner.Kind);
            Assert.Contains("changed", vm.Banner.Text);
            Assert.DoesNotContain("Nothing to undo", vm.Banner.Text);
            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.Equal(new[] { "0:tx1", "1:tx2" }, Placed(rig.Mv.GetLive(rig.Rx)));
        }

        [Fact]
        public async Task Undo_WhileBusy_IsIgnored()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            await vm.TakeAsync();
            vm.Arm(rig.Tx2);
            vm.TapBlock(1);
            await vm.TakeAsync();
            rig.Client.MultiViewCalls.Clear();
            var gate = new TaskCompletionSource();
            rig.Client.DuringCall = entry => entry.StartsWith("multiview:") ? gate.Task : Task.CompletedTask;

            var first = vm.UndoAsync();
            Assert.True(vm.IsBusy);
            var second = vm.UndoAsync();
            gate.SetResult();
            await Task.WhenAll(first, second);

            Assert.Single(rig.Client.MultiViewCalls, c => c.StartsWith("multiview:"));
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async Task Hints_AreWithdrawn_WhenTheModeEnds()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            vm.Disarm();

            vm.BeginMove(0);
            Assert.Equal(BannerKind.Info, vm.Banner.Kind);
            Assert.Contains("Moving block 1", vm.Banner.Text);
            vm.Clear(0); // the moving source is gone
            Assert.Null(vm.MovingFromIndex);
            Assert.Equal(BannerKind.None, vm.Banner.Kind);

            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            vm.Disarm();
            vm.BeginMove(0);
            vm.Disarm(); // cancels the move
            Assert.Null(vm.MovingFromIndex);
            Assert.Equal(BannerKind.None, vm.Banner.Kind);

            vm.BeginMove(0);
            vm.OpenPicker(1);
            Assert.Equal(BannerKind.None, vm.Banner.Kind);

            vm.BeginMove(0);
            vm.TapBlock(1); // the move completes
            Assert.Equal(BannerKind.None, vm.Banner.Kind);
        }

        [Fact]
        public async Task ScreenEvent_ThrowingSubscriber_DoesNotTurnATakeIntoAFailure()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            vm.LiveChanged += _ => throw new InvalidOperationException("subscriber bug");

            await vm.TakeAsync();

            Assert.Equal("On screen", vm.Banner.Text);
            Assert.Equal(BannerKind.Success, vm.Banner.Kind);
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async Task Take_NothingToTake_ReReadsWhatIsOnScreen()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            // The same picture reached the screen by another path; the Composer has not heard yet.
            Assert.Equal(TakeOutcome.Applied, (await rig.Mv.TakeAsync(rig.Rx, vm.Draft!.Clone())).Outcome);
            Assert.True(vm.CanTake);
            rig.Client.MultiViewCalls.Clear();

            await vm.TakeAsync();

            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.Equal(BannerKind.Info, vm.Banner.Kind);
            Assert.Equal("Nothing to change since what is on screen.", vm.Banner.Text);
            Assert.Empty(vm.Changes);
            Assert.Equal(4, vm.LiveBlocks.Count);
            Assert.Equal("TX1", vm.LiveBlocks[0].SourceName);
            Assert.Equal(BlockState.Live, vm.Blocks[0].State);
            Assert.False(vm.CanTake);
        }

        [Fact]
        public async Task Take_ThatThrows_SaysSo_AndUnlocksTheComposer()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            rig.Client.DuringCall = _ => throw new InvalidOperationException("boom");

            await vm.TakeAsync();

            Assert.StartsWith("Take failed", vm.Banner.Text);
            Assert.Contains("boom", vm.Banner.Text);
            Assert.Equal(BannerKind.Error, vm.Banner.Kind);
            Assert.False(vm.IsBusy);
            Assert.Equal(new[] { "0:tx1" }, Placed(vm.Draft));
        }

        [Fact]
        public void ResumedDraft_WithASourceNotInTheSourcesList_IsAnOfflineIssue()
        {
            var rig = new Rig();
            var plan = MultiViewPresets.Build("matrix2x2", 1920, 1080);
            var draft = new MultiViewDraft(plan);
            Assert.True(draft.Place(0, "tx9", "TX9").Ok); // no card for it: cannot be verified

            var vm = rig.Composer(draft);

            Assert.Equal(BlockState.Issue, vm.Blocks[0].State);
            Assert.Equal("TX9 is offline", vm.Blocks[0].IssueText);
            Assert.False(vm.CanTake);
            Assert.Equal("TX9 is offline - clear or replace block 1", vm.TakeBlockedReason);
        }

        // ---- Task 11: keyboard path, automation names, reduced motion ----

        private static int?[] FocusWalk(MultiViewComposerViewModel vm, params FocusDirection[] steps)
        {
            var path = new List<int?>();
            foreach (var step in steps)
            {
                vm.MoveFocus(step);
                path.Add(vm.FocusedBlockIndex);
            }
            return path.ToArray();
        }

        [Fact]
        public async Task Keyboard_ArmMovePlaceClearTake()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            Assert.Null(vm.FocusedBlockIndex);

            // The first arrow focuses the lowest block; arrows then walk the 2x2 and stay put at an edge.
            Assert.Equal(new int?[] { 0, 1, 3, 2, 0, 0 },
                FocusWalk(vm, FocusDirection.Right, FocusDirection.Right, FocusDirection.Down, FocusDirection.Left, FocusDirection.Up, FocusDirection.Left));

            // Arm: nothing is armed, so arming the focused block opens its picker; a source is picked from it.
            vm.ArmFocused();
            Assert.Equal(0, vm.PickerBlockIndex);
            vm.Pick(0, rig.Tx1);
            Assert.Null(vm.PickerBlockIndex);
            Assert.Equal(new[] { "0:tx1" }, Placed(vm.Draft));

            // A source armed (Enter on its card): arrows walk the blocks, Enter places and it stays armed.
            vm.Arm(rig.Tx2);
            vm.ArmFocused(); // already armed: a no-op
            Assert.Same(rig.Tx2, vm.ArmedSource);
            Assert.Null(vm.PickerBlockIndex);
            vm.MoveFocus(FocusDirection.Right);
            vm.PlaceAtFocus();
            vm.MoveFocus(FocusDirection.Down);
            vm.PlaceAtFocus();
            Assert.Equal(new[] { "0:tx1", "1:tx2", "3:tx2" }, Placed(vm.Draft));
            Assert.Same(rig.Tx2, vm.ArmedSource);

            // Delete clears the focused block, staged only.
            vm.Disarm();
            vm.ClearAtFocus();
            Assert.Equal(new[] { "0:tx1", "1:tx2" }, Placed(vm.Draft));
            Assert.Empty(rig.Client.MultiViewCalls);

            // Ctrl+Enter is Take.
            Assert.True(vm.CanTake);
            await vm.TakeAsync();
            Assert.Equal(BannerKind.Success, vm.Banner.Kind);
            Assert.Equal(new[] { "0:tx1", "1:tx2" }, Placed(rig.Mv.GetLive(rig.Rx)));
        }

        [Fact]
        public async Task Keyboard_PlaceAtFocus_FollowsATap_OnEmptyPlacedAndMovingBlocks()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.FocusBlock(2);

            vm.PlaceAtFocus(); // nothing armed, empty block: the picker opens
            Assert.Equal(2, vm.PickerBlockIndex);
            vm.Pick(2, rig.Tx1);

            vm.PlaceAtFocus(); // a placed block, nothing armed: its Replace / Move strip opens
            Assert.True(vm.Blocks[2].IsSelected);
            vm.BeginMove(2); // the strip's Move
            vm.FocusBlock(3);
            vm.PlaceAtFocus(); // moving: the focused block is the destination
            Assert.Equal(new[] { "3:tx1" }, Placed(vm.Draft));
            Assert.Null(vm.MovingFromIndex);
        }

        [Fact]
        public async Task Keyboard_ActionsWithNothingFocused_DoNothing()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            vm.Disarm();

            vm.PlaceAtFocus();
            vm.ClearAtFocus();
            vm.ArmFocused();

            Assert.Equal(new[] { "0:tx1" }, Placed(vm.Draft)); // Delete with no focus never clears a block
            Assert.Null(vm.PickerBlockIndex);
            Assert.Null(vm.FocusedBlockIndex);
        }

        [Theory]
        [InlineData("matrix2x2", "Right Down Left Up Up Left", new[] { 1, 3, 2, 0, 0, 0 })]
        [InlineData("side_by_side", "Right Right Left Down Up", new[] { 1, 1, 0, 0, 0 })]
        // PIP: API index 0 is the inset (bottom right), 1 the background.
        [InlineData("pip", "Right Left Down Left Up Right", new[] { 0, 1, 0, 1, 1, 0 })]
        // 1+3: index 0 is the large block, 1 to 3 the small column. Up from the large block has nothing above it.
        [InlineData("quad_1_3", "Right Down Down Down Left Up Up Up Up", new[] { 2, 3, 3, 3, 0, 0, 0, 0, 0 })]
        public async Task MoveFocus_WalksTheLayoutByItsGeometry_AndStaysPutAtAnEdge(string layout, string steps, int[] expected)
        {
            var rig = new Rig();
            var vm = rig.Composer();
            await vm.SelectLayoutAsync(layout);
            vm.FocusBlock(0); // the walk starts on the lowest block

            var directions = steps.Split(' ').Select(Enum.Parse<FocusDirection>).ToArray();

            Assert.Equal(expected.Cast<int?>(), FocusWalk(vm, directions));
        }

        [Fact]
        public void MoveFocus_OnAnEmptyComposer_DoesNothing()
        {
            var rig = new Rig();
            var vm = rig.Composer(); // Step 1 not done: no blocks

            vm.MoveFocus(FocusDirection.Right);

            Assert.Null(vm.FocusedBlockIndex);
        }

        [Fact]
        public async Task FocusedBlock_IsFlagged_AndOnlyItIsTheTabStop()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();

            // Before any focus the first block is the one Tab stop (roving: one stop for the whole canvas).
            Assert.Equal(new[] { true, false, false, false }, vm.Blocks.Select(b => b.IsTabStop));
            Assert.All(vm.Blocks, b => Assert.False(b.IsFocusedBlock));

            vm.FocusBlock(3);

            Assert.Equal(new[] { false, false, false, true }, vm.Blocks.Select(b => b.IsTabStop));
            Assert.Equal(new[] { false, false, false, true }, vm.Blocks.Select(b => b.IsFocusedBlock));
            Assert.Equal(3, vm.FocusedBlockIndex);
        }

        [Fact]
        public async Task FocusedBlock_FollowsTheLayout_AndIsForgottenWhenItsBlockIsGone()
        {
            var rig = new Rig();
            var vm = rig.Composer();
            await vm.SelectLayoutAsync("quad_1_3");
            vm.FocusBlock(3);

            await vm.SelectLayoutAsync("pip"); // two blocks: index 3 no longer exists

            Assert.Null(vm.FocusedBlockIndex);
            Assert.Equal(new[] { true, false }, vm.Blocks.Select(b => b.IsTabStop));
        }

        [Fact]
        public async Task Escape_ClimbsTheLadder_PickerThenMoveThenArmedThenNothing()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            vm.Disarm();

            // 1. the picker closes, nothing else
            vm.OpenPicker(1);
            Assert.True(vm.Escape());
            Assert.Null(vm.PickerBlockIndex);

            // 2. a move is cancelled
            vm.BeginMove(0);
            Assert.True(vm.Escape());
            Assert.Null(vm.MovingFromIndex);
            Assert.Equal(new[] { "0:tx1" }, Placed(vm.Draft));

            // 3. an armed source is disarmed, the draft is kept
            vm.Arm(rig.Tx2);
            Assert.True(vm.Escape());
            Assert.Null(vm.ArmedSource);
            Assert.Equal(new[] { "0:tx1" }, Placed(vm.Draft));

            // 4. an open Replace / Move strip closes
            vm.TapBlock(0);
            Assert.True(vm.Blocks[0].IsSelected);
            Assert.True(vm.Escape());
            Assert.False(vm.Blocks[0].IsSelected);

            // 5. nothing left to cancel: Esc does nothing, and the draft and Composer stay as they are
            Assert.False(vm.Escape());
            Assert.Equal(new[] { "0:tx1" }, Placed(vm.Draft));
            Assert.Equal("matrix2x2", vm.SelectedLayoutId);
        }

        [Fact]
        public async Task PickerAndArmedSource_NeverCoexist_SoEscapeHasOneRungToClimb()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            vm.OpenPicker(1);
            vm.Arm(rig.Tx1); // arming closes the picker: the two never coexist
            Assert.Null(vm.PickerBlockIndex);

            vm.OpenPicker(2); // opening the picker puts the armed source down
            Assert.Null(vm.ArmedSource);
            Assert.True(vm.Escape());
            Assert.Null(vm.PickerBlockIndex);
            Assert.False(vm.Escape());
        }

        [Fact]
        public async Task Blocks_HaveAutomationNames()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();

            // The brief's example, exactly.
            Assert.Equal("Block 2, 960 by 540, empty", vm.Blocks[1].AutomationName);
            Assert.Equal(new[]
            {
                "Block 1, 960 by 540, empty", "Block 2, 960 by 540, empty", "Block 3, 960 by 540, empty", "Block 4, 960 by 540, empty",
            }, vm.Blocks.Select(b => b.AutomationName));

            // Placed (staged): the source and "staged"; a duplicate says which block it repeats.
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            vm.TapBlock(2);
            vm.Disarm();
            Assert.Equal("Block 1, 960 by 540, TX1, staged", vm.Blocks[0].AutomationName);
            Assert.Equal("Block 3, 960 by 540, TX1, same as block 1, staged", vm.Blocks[2].AutomationName);

            // On screen: no "staged".
            await vm.TakeAsync();
            Assert.Equal("Block 1, 960 by 540, TX1", vm.Blocks[0].AutomationName);
            Assert.Equal("Block 3, 960 by 540, TX1, same as block 1", vm.Blocks[2].AutomationName);
            Assert.Equal("Block 2, 960 by 540, empty", vm.Blocks[1].AutomationName);

            // Cleared on a block that is on screen: empty, and the change is staged.
            vm.Clear(0);
            Assert.Equal("Block 1, 960 by 540, empty, staged", vm.Blocks[0].AutomationName);
        }

        [Fact]
        public async Task Blocks_AutomationNames_SayWhyABlockCannotTakeTheArmedSource_OrWhatBlocksTake()
        {
            var rig = new Rig();
            var vm = rig.Composer();
            await vm.SelectLayoutAsync("quad_1_3"); // block 1 is large, blocks 2-4 are small
            vm.Arm(rig.Tx1);
            vm.TapBlock(0);

            string reason = vm.Blocks[1].UnavailableReason!;
            Assert.Equal($"Block 2, {vm.Blocks[1].Width} by {vm.Blocks[1].Height}, empty, {reason}", vm.Blocks[1].AutomationName);
            Assert.StartsWith("Block 1, ", vm.Blocks[0].AutomationName);
            Assert.DoesNotContain("TX1 is in block 1", vm.Blocks[0].AutomationName);

            // An offline encoder in a block: the problem is in the name.
            vm.Disarm();
            rig.Tx1.SetOnlineState(false);
            Assert.Equal($"Block 1, {vm.Blocks[0].Width} by {vm.Blocks[0].Height}, TX1, TX1 is offline", vm.Blocks[0].AutomationName);
        }

        [Fact]
        public async Task Blocks_AutomationName_IsRaised_WhenTheBlockChanges()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            var raised = new List<string?>();
            vm.Blocks[0].PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Arm(rig.Tx1);
            vm.TapBlock(0);

            Assert.Contains(nameof(ComposerBlockViewModel.AutomationName), raised);
        }

        [Fact]
        public async Task LayoutTiles_HaveAutomationNames_WithTheirSelectedState()
        {
            var rig = new Rig();
            var vm = rig.Composer();
            Assert.Equal("Matrix 2x2, not selected", vm.LayoutOptions[0].AutomationName);

            var raised = new List<string?>();
            vm.LayoutOptions[0].PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            await vm.SelectLayoutAsync("matrix2x2");

            Assert.Equal("Matrix 2x2, selected", vm.LayoutOptions[0].AutomationName);
            Assert.Equal("PIP, not selected", vm.LayoutOptions[1].AutomationName);
            Assert.Contains(nameof(ComposerLayoutOptionViewModel.AutomationName), raised);
        }

        [Fact]
        public void SourceCards_HaveAutomationNames_WithTheirState()
        {
            var rig = new Rig();
            var raised = new List<string?>();
            rig.Tx1.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            Assert.Equal("TX1, online", rig.Tx1.AutomationName);

            rig.Tx1.IsArmed = true;
            Assert.Equal("TX1, online, placing - press Enter to stop", rig.Tx1.AutomationName);
            rig.Tx1.UsedInBlocks = 1;
            Assert.Equal("TX1, online, placing - press Enter to stop, in 1 block", rig.Tx1.AutomationName);
            rig.Tx1.IsArmed = false;
            rig.Tx1.UsedInBlocks = 3;
            Assert.Equal("TX1, online, in 3 blocks", rig.Tx1.AutomationName);
            rig.Tx1.SetOnlineState(false);
            Assert.Equal("TX1, offline, in 3 blocks", rig.Tx1.AutomationName);
            Assert.Contains(nameof(SourceCardViewModel.AutomationName), raised);
        }

        [Fact]
        public async Task ExplainTake_SaysWhyTakeIsOff_AndTheLineGoesWhenItNoLongerHolds()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();

            vm.ExplainTake();

            Assert.Equal(BannerKind.Info, vm.Banner.Kind);
            Assert.Equal("Place at least one source (Step 2).", vm.Banner.Text);

            vm.Arm(rig.Tx1); // the arm hint replaces it, as any hint would
            vm.TapBlock(0);
            vm.Disarm();
            Assert.Equal(BannerKind.None, vm.Banner.Kind);
            Assert.True(vm.CanTake);

            vm.ExplainTake(); // Take is available: nothing to explain
            Assert.Equal(BannerKind.None, vm.Banner.Kind);

            rig.Tx1.SetOnlineState(false);
            vm.ExplainTake();
            Assert.Equal("TX1 is offline - clear or replace block 1", vm.Banner.Text);
            rig.Tx1.SetOnlineState(true); // the reason is gone: so is the line
            Assert.Equal(BannerKind.None, vm.Banner.Kind);
        }

        [Fact]
        public async Task TakeButton_AutomationName_AddsTheReasonWhileItIsDisabled()
        {
            var rig = new Rig();
            var vm = await rig.Matrix();
            Assert.Equal("TAKE 0 CHANGES TO SCREEN. Place at least one source (Step 2).", vm.TakeAutomationName);

            vm.Arm(rig.Tx1);
            vm.TapBlock(0);
            Assert.Equal("TAKE 1 CHANGE TO SCREEN", vm.TakeAutomationName);

            vm.Disarm();
            rig.Tx1.SetOnlineState(false);
            Assert.Equal("TAKE 1 CHANGE TO SCREEN. TX1 is offline - clear or replace block 1", vm.TakeAutomationName);

            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            rig.Tx1.SetOnlineState(true);
            Assert.Contains(nameof(MultiViewComposerViewModel.TakeAutomationName), raised);
        }

        [Fact]
        public void Transitions_AreSkipped_WhenWindowsAnimationsAreOff()
        {
            var rig = new Rig();
            bool animations = false;
            var off = new MultiViewComposerViewModel(rig.RxVm, rig.Mv, () => rig.Sources, rig.Layouts, null, () => rig.Now, () => animations);

            Assert.False(off.AnimationsEnabled);
            Assert.Equal(TimeSpan.Zero, off.FrameTransitionDuration);
            Assert.Equal(TimeSpan.Zero, off.FadeDuration);
            Assert.Equal(TimeSpan.Zero, off.TransitionDuration(TimeSpan.FromMilliseconds(123)));

            // The setting is read when it is needed: turning animations on later takes effect at once.
            animations = true;
            Assert.True(off.AnimationsEnabled);
            Assert.Equal(TimeSpan.FromMilliseconds(400), off.FrameTransitionDuration); // staged frame -> live frame (spec 8)
            Assert.Equal(TimeSpan.FromMilliseconds(150), off.FadeDuration); // fades (spec 9)
            Assert.Equal(TimeSpan.FromMilliseconds(123), off.TransitionDuration(TimeSpan.FromMilliseconds(123)));
        }

        [Fact]
        public void Transitions_DefaultToTheWindowsSetting_WhenNoSourceIsInjected()
        {
            var rig = new Rig();
            var vm = rig.Composer();

            Assert.Equal(System.Windows.SystemParameters.ClientAreaAnimation, vm.AnimationsEnabled);
        }
    }
}
