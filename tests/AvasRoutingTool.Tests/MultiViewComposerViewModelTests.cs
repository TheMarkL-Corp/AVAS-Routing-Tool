using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using AvasRoutingTool.ViewModels;
using Xunit;

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

        // ---- the 18 listed tests ----

        [Fact]
        public async Task NewComposer_IsAtStepOne_WithNothingStaged()
        {
            var rig = new Rig();

            var vm = rig.Composer();

            Assert.False(vm.StepLayoutDone);
            Assert.False(vm.StepPlaceDone);
            Assert.Empty(vm.Blocks);
            Assert.False(vm.CanTake);
            Assert.Equal("Choose a layout (Step 1).", vm.TakeBlockedReason);
            Assert.Equal(new[] { "matrix2x2", "pip", "side_by_side", "quad_1_3" }, vm.LayoutOptions.Select(l => l.Id));
            await Task.CompletedTask;
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

            Assert.Equal("The display changed - review the new layout, then take.", vm.Banner.Text);
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
    }
}
