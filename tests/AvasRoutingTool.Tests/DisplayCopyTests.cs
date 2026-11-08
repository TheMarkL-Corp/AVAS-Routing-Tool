using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using AvasRoutingTool.ViewModels;
using Xunit;

// NOTE: test names in this file must not contain the substring "Live" (in any case): the shared filter
// FullyQualifiedName!~Live (which keeps hardware tests out of the normal run) would silently skip them.
namespace AvasRoutingTool.Tests
{
    /// <summary>Copy a display onto another (spec section 15): the hardware sequence, the target's 10 s Undo, the overview state.</summary>
    public class DisplayCopyTests
    {
        private const string Rx1Mac = "74fe483c059e";
        private const string Rx1Name = "RX1_c059e_SFPA";
        private const string PaxMac = "74fe4800355a";
        private const string PaxName = "PAX355S_SFP A";
        private const string Rx3Mac = "74fe48000003";
        private const string Rx4Mac = "74fe48000004";
        private const string Tx2Mac = "74fe488b07bb";
        private const string Tx2Name = "TX2_DUAL LINK_SFPA";
        private const string VegaMac = "74fe48000001";
        private const string VegaName = "VEGA_1100";
        private const string StaleNotice = "Screen changed - draft is based on an older picture";

        private static readonly string FullHd = TestEdids.Build((1920, 1080, 60, false));

        private sealed class Rig
        {
            public MockSdvoeClient Client { get; } = new();
            public MultiViewOrchestrator MultiView { get; }
            public MainViewModel Vm { get; }
            public AvasDevice Rx1 { get; } = Display(Rx1Mac, Rx1Name);
            public AvasDevice Pax { get; } = Display(PaxMac, PaxName);
            public AvasDevice Rx3 { get; } = Display(Rx3Mac, "RX3");
            public AvasDevice Rx4 { get; } = Display(Rx4Mac, "RX4");
            public DisplayCardViewModel Rx1Vm { get; }
            public DisplayCardViewModel PaxVm { get; }
            public DisplayCardViewModel Rx3Vm { get; }
            public DisplayCardViewModel Rx4Vm { get; }
            public SourceCardViewModel Tx2Vm { get; } = Source(Tx2Mac, Tx2Name);
            public SourceCardViewModel VegaVm { get; } = Source(VegaMac, VegaName);
            public DateTime Now { get; set; } = new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);

            public Rig(bool more = false)
            {
                MultiView = new MultiViewOrchestrator(Client);
                Vm = new MainViewModel(new ConfigService(), Client, new MockRtpReceiver(), new MulticastIpManager(),
                    new RoutingOrchestrator(Client), MultiView, () => Now);
                Rx1Vm = new DisplayCardViewModel(Rx1, Vm.UnrouteVideoAsync);
                PaxVm = new DisplayCardViewModel(Pax, Vm.UnrouteVideoAsync);
                Rx3Vm = new DisplayCardViewModel(Rx3, Vm.UnrouteVideoAsync);
                Rx4Vm = new DisplayCardViewModel(Rx4, Vm.UnrouteVideoAsync);
                Vm.Displays.Add(Rx1Vm);
                Vm.Displays.Add(PaxVm);
                if (more)
                {
                    Vm.Displays.Add(Rx3Vm);
                    Vm.Displays.Add(Rx4Vm);
                }
                Vm.Sources.Add(Tx2Vm);
                Vm.Sources.Add(VegaVm);
                // Discovered cards get a rename handler from MainViewModel; the hand-built ones get a no-op so their pencils look as in the app.
                foreach (var display in new[] { Rx1Vm, PaxVm, Rx3Vm, Rx4Vm }) display.RenameHandler ??= () => Task.CompletedTask;
                foreach (var source in new[] { Tx2Vm, VegaVm }) source.RenameHandler ??= () => Task.CompletedTask;
                Client.DisplayEdids[Rx1Mac] = TestEdids.ViewSonic4k;
                Client.DisplayEdids[PaxMac] = TestEdids.ViewSonic4k;
                Client.DisplayEdids[Rx3Mac] = FullHd;
            }

            /// <summary>RX1 found running a matrix this tool made: TX2 in blocks 1 and 3 (one surface), VEGA in block 2.</summary>
            public Task RestoreMatrixOnRx1Async(int width = 3840, int height = 2160)
            {
                Client.ReceiverStates[Rx1Mac] = new RxState(Rx1Mac, Rx1Name, "MULTIVIEW", width, height, 60,
                    new[] { Sub(1, Tx2Mac, Tx2Name), Sub(2, VegaMac, VegaName) },
                    new RxLayout($"avas_{Rx1Mac}_matrix2x2_{width}x{height}", width, height,
                        new[] { new RxWindow(0, 1), new RxWindow(1, 2), new RxWindow(2, 1) }));
                return Vm.RestoreDisplayAsync(Rx1Vm);
            }

            /// <summary>PAX found running its own side-by-side: VEGA left (subscription 3), TX2 right (subscription 4).</summary>
            public Task RestoreSideBySideOnPaxAsync()
            {
                Client.ReceiverStates[PaxMac] = new RxState(PaxMac, PaxName, "MULTIVIEW", 3840, 2160, 60,
                    new[] { Sub(3, VegaMac, VegaName), Sub(4, Tx2Mac, Tx2Name) },
                    new RxLayout($"avas_{PaxMac}_side_by_side_3840x2160", 3840, 2160,
                        new[] { new RxWindow(0, 3), new RxWindow(1, 4) }));
                return Vm.RestoreDisplayAsync(PaxVm);
            }

            /// <summary>RX1 found running a layout made elsewhere, with real window geometry and one black window.</summary>
            public Task RestoreExternalOnRx1Async()
            {
                Client.ReceiverStates[Rx1Mac] = new RxState(Rx1Mac, Rx1Name, "MULTIVIEW", 3840, 2160, 60,
                    new[] { Sub(3, Tx2Mac, Tx2Name), new RxSubscription(5, true, "STREAMING", VegaMac, VegaName, 0) },
                    new RxLayout("bavm_custom", 3840, 2160, new[]
                    {
                        new RxWindow(0, 3, 0, 0, 1920, 1080),
                        new RxWindow(1, 5, 1920, 0, 1920, 1080, 16, 8),
                        new RxWindow(2, 255, 0, 1080, 1920, 1080),
                    }));
                return Vm.RestoreDisplayAsync(Rx1Vm);
            }

            /// <summary>The card's display found in the given state.</summary>
            public Task RestoreAsync(DisplayCardViewModel card, RxState state)
            {
                Client.ReceiverStates[card.MacAddress] = state;
                return Vm.RestoreDisplayAsync(card);
            }

            private static AvasDevice Display(string mac, string name) =>
                new() { DeviceId = mac, DeviceName = name, IsReceiver = true, IsActive = true };

            private static SourceCardViewModel Source(string mac, string name) =>
                new(new AvasDevice { DeviceId = mac, DeviceName = name, VendorId = 105, IsTransmitter = true, IsActive = true });
        }

        private static RxSubscription Sub(int index, string mac, string name) => new(index, true, "STREAMING", mac, name, 1);

        private static string[] Placed(MultiViewDraft? draft) =>
            draft == null
                ? Array.Empty<string>()
                : draft.Placements.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.SourceMac}").ToArray();

        private static (Task Entered, Action Release) HoldFirstCall(MockSdvoeClient client, string prefix)
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.DuringCall = entry =>
                entry.StartsWith(prefix) && entered.TrySetResult() ? release.Task : Task.CompletedTask;
            return (entered.Task, () => release.TrySetResult());
        }

        private static async Task Within(Task task, string what)
        {
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(ReferenceEquals(done, task), $"{what} did not finish: deadlock?");
            await task;
        }

        private static string MatrixLayout(string mac, int w = 3840, int h = 2160) => $"avas_{mac}_matrix2x2_{w}x{h}";

        // ---- the listed tests ----

        [Fact]
        public async Task CopyMultiView_ToSameResolution_LoadsTheSourcesWindowsAndSubscriptions_OnTheTarget()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            Assert.True(rig.Vm.CopyDecisionFor(rig.PaxVm).Allowed);
            rig.Client.MultiViewCalls.Clear();

            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm));

            // Ruling R5: a layout this tool made is copied under the target's own preset name (restores as ours).
            string layout = MatrixLayout(PaxMac);
            Assert.Equal(new[] { $"layout:{layout}", $"multiview:{PaxMac}:{layout}:{Tx2Mac}@1,{VegaMac}@2" }, rig.Client.MultiViewCalls);
            Assert.Equal((3840, 2160), rig.Client.LastMultiViewSize);
            Assert.Equal(60, rig.Client.LastFps);
            var source = SurfacePlanner.Plan(rig.MultiView.GetLive(rig.Rx1)!, Rx1Mac);
            Assert.Equal(source.Windows.Select(w => (w.Slot.Index, w.Slot.X, w.Slot.Y, w.Slot.Width, w.Slot.Height, w.Surface)),
                rig.Client.LastSurfacePlan!.Windows.Select(w => (w.Slot.Index, w.Slot.X, w.Slot.Y, w.Slot.Width, w.Slot.Height, w.Surface)));
            Assert.True(MultiViewPresets.TryParseName(layout, out string id, out _, out _));
            Assert.Equal("matrix2x2", id);
            Assert.All(rig.Client.LastSubscriptions!, s => Assert.Equal(1, s.StreamIndex)); // the scaled stream, as the source

            Assert.Equal(new[] { $"0:{Tx2Mac}", $"1:{VegaMac}", $"2:{Tx2Mac}" }, Placed(rig.MultiView.GetLive(rig.Pax)));
            Assert.True(rig.PaxVm.IsMultiView);
            Assert.Equal("MATRIX 2X2 · 3/4", rig.PaxVm.ModeBadge);
            Assert.Equal(new[] { $"0:{Tx2Mac}", $"1:{VegaMac}", $"2:{Tx2Mac}" }, Placed(rig.MultiView.GetLive(rig.Rx1))); // the source is untouched
            Assert.False(rig.MultiView.CanUndo(rig.Pax)); // the copy has its own Undo, not the Composer's
        }

        [Fact]
        public async Task CopyMultiView_ToHigherResolution_RunsAtTheSourcesFormat()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async(1920, 1080);
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            Assert.Equal("Runs at 1920x1080 @ 60 on this 3840x2160 display", rig.PaxVm.CopyTargetText);
            rig.Client.MultiViewCalls.Clear();

            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm));

            string layout = MatrixLayout(PaxMac, 1920, 1080);
            Assert.Equal(new[] { $"layout:{layout}", $"multiview:{PaxMac}:{layout}:{Tx2Mac}@1,{VegaMac}@2" }, rig.Client.MultiViewCalls);
            Assert.Equal((1920, 1080), rig.Client.LastMultiViewSize);
            Assert.Equal(60, rig.Client.LastFps);
            Assert.Equal((1920, 1080), (rig.MultiView.GetCanvas(rig.Pax)!.Width, rig.MultiView.GetCanvas(rig.Pax)!.Height));
            Assert.Contains("1920x1080@60", rig.PaxVm.CanvasInfo);
        }

        [Fact]
        public async Task CopySingleView_RoutesTheSameEncoder()
        {
            var rig = new Rig();
            await rig.Vm.RouteVideoAsync(rig.Tx2Vm.Device, rig.Rx1);
            rig.Client.LastVideoRx = rig.Client.LastVideoTx = null;
            rig.Client.MultiViewCalls.Clear();
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            Assert.Equal($"Copy {Rx1Name} - Single View ({Tx2Name}) - 3840x2160", rig.PaxVm.CopyTargetText);

            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm));

            Assert.Equal(PaxMac, rig.Client.LastVideoRx);
            Assert.Equal(Tx2Mac, rig.Client.LastVideoTx);
            Assert.Equal(new[] { $"fastswitch:{PaxMac}:3840x2160@60" }, rig.Client.MultiViewCalls); // the copy only sets the mode, no multiview scaffolding
            Assert.Equal(Tx2Mac, rig.PaxVm.ActiveSourceId);
            Assert.Equal("SINGLE VIEW", rig.PaxVm.ModeBadge);
            Assert.Equal(Tx2Name, rig.PaxVm.SourceLabel);
        }

        [Fact]
        public async Task CopyDisplayAsync_Blocked_SendsNothing()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            rig.Client.DisplayEdids[PaxMac] = FullHd;
            rig.Client.MultiViewCalls.Clear();

            Assert.False(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm));

            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.Null(rig.Client.LastVideoTx);
            Assert.Empty(rig.Client.Leaves);
            Assert.False(rig.MultiView.IsMultiView(rig.Pax));
            Assert.Null(rig.PaxVm.UndoToastText);
            Assert.Null(rig.PaxVm.LastError); // a refusal is the card's copy text, not an error
            Assert.Equal(1, rig.Client.EdidReadsBy.Count(m => m == PaxMac)); // not armed: the target was read for this copy
        }

        [Fact]
        public async Task Copy_ShowsAnUndoToastOnTheTarget_For10Seconds()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);

            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm));

            Assert.Equal($"{PaxName} now shows {Rx1Name}'s Matrix 2x2", rig.PaxVm.UndoToastText);
            Assert.True(rig.PaxVm.CanUndoScreen);
            Assert.True(rig.PaxVm.UndoScreenCommand.CanExecute(null));
            Assert.Null(rig.Rx1Vm.UndoToastText); // only on the target

            rig.Now += TimeSpan.FromSeconds(9.9);
            rig.Vm.Tick();
            Assert.True(rig.PaxVm.CanUndoScreen);
            Assert.NotNull(rig.MultiView.PreviousScreen(rig.Pax));

            rig.Now += TimeSpan.FromSeconds(0.1);
            rig.Vm.Tick();
            Assert.False(rig.PaxVm.CanUndoScreen);
            Assert.Null(rig.PaxVm.UndoToastText);
            Assert.Null(rig.MultiView.PreviousScreen(rig.Pax));
            Assert.False(await rig.Vm.UndoScreenAsync(rig.PaxVm)); // expired: nothing to put back
        }

        [Fact]
        public async Task UndoScreen_PutsBackWhatTheTargetShowedBefore()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            await rig.Vm.RouteVideoAsync(rig.VegaVm.Device, rig.Pax);
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm));
            Assert.Equal(ScreenKind.Single, rig.MultiView.PreviousScreen(rig.Pax)!.Kind);
            rig.Client.MultiViewCalls.Clear();
            rig.Client.LastVideoRx = rig.Client.LastVideoTx = null;
            rig.Client.ReceiverStates.Remove(PaxMac); // Pax runs the copied multiview now, so it is not "already in Fast Switch"

            rig.PaxVm.UndoScreenCommand.Execute(null);
            await Within(WaitUntil(() => rig.PaxVm.ActiveSourceId == VegaMac), "Undo");

            Assert.Equal(new[] { $"fastswitch:{PaxMac}:3840x2160@60" }, rig.Client.MultiViewCalls);
            Assert.Equal(PaxMac, rig.Client.LastVideoRx);
            Assert.Equal(VegaMac, rig.Client.LastVideoTx);
            Assert.False(rig.MultiView.IsMultiView(rig.Pax));
            Assert.False(rig.PaxVm.IsMultiView);
            Assert.Equal(VegaMac, rig.PaxVm.ActiveSourceId);
            Assert.Null(rig.PaxVm.UndoToastText);
            Assert.Null(rig.MultiView.PreviousScreen(rig.Pax)); // gone after Undo
        }

        [Fact]
        public async Task Copy_MarksTheTargetsDraftStale()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            await rig.Vm.OpenComposerAsync(rig.PaxVm, "matrix2x2");
            rig.Vm.ActiveComposer!.Arm(rig.VegaVm);
            rig.Vm.ActiveComposer!.TapBlock(3);
            rig.Vm.CloseComposer();
            Assert.True(rig.PaxVm.HasDraft);
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);

            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm));

            Assert.True(rig.PaxVm.HasDraft); // never overwritten
            Assert.Equal(StaleNotice, rig.PaxVm.DraftNotice);
            Assert.True(rig.PaxVm.IsMultiView);

            await rig.Vm.OpenComposerAsync(rig.PaxVm); // resuming says so too
            Assert.Equal("matrix2x2", rig.Vm.ActiveComposer!.SelectedLayoutId);
            Assert.Equal(new[] { $"3:{VegaMac}" }, Placed(rig.Vm.ActiveComposer.Draft));
            Assert.Equal(StaleNotice, rig.Vm.ActiveComposer.Banner.Text);
            Assert.Equal(BannerKind.Warning, rig.Vm.ActiveComposer.Banner.Kind);
            Assert.Null(rig.PaxVm.DraftNotice);
        }

        [Fact]
        public void Copy_WhileBusy_IsIgnored() => UiThread.Run(async () =>
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            rig.Client.MultiViewCalls.Clear();
            var (entered, release) = HoldFirstCall(rig.Client, "multiview:");

            var first = rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm);
            await Within(entered, "Copy reaching set:multiview");
            var second = rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm); // the double tap
            Assert.True(second.IsCompleted);
            Assert.False(await second);

            release();
            await Within(first, "Copy");
            Assert.True(await first);
            Assert.Single(rig.Client.MultiViewCalls, c => c.StartsWith("layout:"));
            Assert.Single(rig.Client.MultiViewCalls, c => c.StartsWith("multiview:"));
        });

        [Fact]
        public async Task ArmDisplay_ReadsEachTargetEdidOnce_AndUpdatesEveryCardsCopyState()
        {
            var rig = new Rig(more: true);
            await rig.RestoreMatrixOnRx1Async();
            rig.Rx4Vm.SetOnlineState(false);
            Assert.All(rig.Vm.Displays, d => Assert.False(d.IsCopyTargetAllowed || d.IsCopyTargetBlocked)); // nothing armed

            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);

            Assert.Same(rig.Rx1Vm, rig.Vm.ArmedDisplay);
            Assert.True(rig.Rx1Vm.IsCopySource);
            Assert.Equal(new[] { Rx3Mac, PaxMac }, rig.Client.EdidReadsBy.OrderBy(m => m, StringComparer.Ordinal)); // online targets only, once each

            Assert.True(rig.PaxVm.IsCopyTargetAllowed);
            Assert.False(rig.PaxVm.IsCopyTargetBlocked);
            Assert.Equal($"Copy {Rx1Name} - Matrix 2x2 ({Tx2Name}, {VegaName}) - 3840x2160", rig.PaxVm.CopyTargetText);

            Assert.True(rig.Rx3Vm.IsCopyTargetBlocked);
            Assert.False(rig.Rx3Vm.IsCopyTargetAllowed);
            Assert.Equal($"RX3 is 1920x1080 - {Rx1Name}'s picture is 3840x2160. Copy needs a display at least that large.", rig.Rx3Vm.CopyTargetText);

            Assert.True(rig.Rx4Vm.IsCopyTargetBlocked);
            Assert.Equal("RX4 is offline", rig.Rx4Vm.CopyTargetText);

            Assert.False(rig.Rx1Vm.IsCopyTargetAllowed || rig.Rx1Vm.IsCopyTargetBlocked); // the source itself: no affordance
            Assert.Equal("Drop on another display", rig.Vm.CopyDecisionFor(rig.Rx1Vm).Reason);

            rig.Vm.CopyDecisionFor(rig.PaxVm);
            rig.Vm.CopyDecisionFor(rig.Rx3Vm);
            Assert.Equal(2, rig.Client.EdidReads); // cached for this arm

            rig.Vm.DisarmDisplay();

            Assert.Null(rig.Vm.ArmedDisplay);
            Assert.False(rig.Rx1Vm.IsCopySource);
            Assert.All(rig.Vm.Displays, d =>
            {
                Assert.False(d.IsCopyTargetAllowed);
                Assert.False(d.IsCopyTargetBlocked);
                Assert.Equal(string.Empty, d.CopyTargetText);
            });
        }

        [Fact]
        public async Task ArmedDisplay_TapAllowedTarget_CopiesImmediately_AndDisarms()
        {
            var rig = new Rig(more: true);
            await rig.RestoreMatrixOnRx1Async();
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            rig.Client.MultiViewCalls.Clear();

            await rig.Vm.TapDisplayAsync(rig.PaxVm);

            Assert.Contains($"multiview:{PaxMac}:{MatrixLayout(PaxMac)}:{Tx2Mac}@1,{VegaMac}@2", rig.Client.MultiViewCalls);
            Assert.Null(rig.Vm.ArmedDisplay);
            Assert.False(rig.Rx1Vm.IsCopySource);
            Assert.False(rig.PaxVm.IsCopyTargetAllowed);
            Assert.False(rig.Rx3Vm.IsCopyTargetBlocked);
            Assert.True(rig.PaxVm.CanUndoScreen);
            Assert.Equal(3, rig.Client.EdidReads); // the tap used the arm's reads (PAX, RX3, RX4)
        }

        [Fact]
        public async Task ArmedDisplay_TapBlockedTarget_DoesNothing_ButShowsTheReason()
        {
            var rig = new Rig(more: true);
            await rig.RestoreMatrixOnRx1Async();
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            rig.Client.MultiViewCalls.Clear();

            await rig.Vm.TapDisplayAsync(rig.Rx3Vm);
            await rig.Vm.TapDisplayAsync(rig.Rx1Vm); // the source itself: nothing either

            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.Null(rig.Client.LastVideoTx);
            Assert.Same(rig.Rx1Vm, rig.Vm.ArmedDisplay); // still armed
            Assert.True(rig.Rx3Vm.IsCopyTargetBlocked);
            Assert.StartsWith("RX3 is 1920x1080", rig.Rx3Vm.CopyTargetText);
            Assert.Null(rig.Rx3Vm.LastError);
            Assert.True(rig.PaxVm.IsCopyTargetAllowed);
        }

        [Fact]
        public async Task SourceShowingNothing_CannotBeArmed()
        {
            var rig = new Rig();

            Assert.False(rig.Rx1Vm.CanCopyFrom);
            Assert.Equal($"{Rx1Name} shows nothing to copy", rig.Rx1Vm.CopyFromBlockedText);
            Assert.False(rig.Rx1Vm.CopyLayoutCommand.CanExecute(null));
            Assert.Equal(ScreenKind.Nothing, rig.Vm.CurrentScreen(rig.Rx1Vm).Kind);

            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);

            Assert.Null(rig.Vm.ArmedDisplay);
            Assert.False(rig.Rx1Vm.IsCopySource);
            Assert.Equal(0, rig.Client.EdidReads);

            await rig.Vm.RouteVideoAsync(rig.Tx2Vm.Device, rig.Rx1);
            Assert.True(rig.Rx1Vm.CanCopyFrom);
            Assert.Equal(string.Empty, rig.Rx1Vm.CopyFromBlockedText);
        }

        [Fact]
        public void CopyDragEffects_AllowedIsCopy_BlockedIsNone()
        {
            var allowed = new CopyDecision(true, "Copy RX1 - Matrix 2x2 (TX2) - 3840x2160", null, null);
            var noted = new CopyDecision(true, "Copy RX1 - Matrix 2x2 (TX2) - 1920x1080", "Runs at 1920x1080 @ 60 on this 3840x2160 display", null);
            var blocked = new CopyDecision(false, "Copy RX1 - Matrix 2x2 (TX2) - 3840x2160", null, "PAX355S is offline");

            Assert.Equal(DragDropEffects.Copy, CopyDragFeedback.Effects(allowed));
            Assert.Equal(DragDropEffects.Copy, CopyDragFeedback.Effects(noted));
            Assert.Equal(DragDropEffects.None, CopyDragFeedback.Effects(blocked));
            Assert.Equal("Copy RX1 - Matrix 2x2 (TX2) - 3840x2160", CopyDragFeedback.Tooltip(allowed));
            Assert.Equal("Runs at 1920x1080 @ 60 on this 3840x2160 display", CopyDragFeedback.Tooltip(noted));
            Assert.Equal("PAX355S is offline", CopyDragFeedback.Tooltip(blocked));
        }

        // ---- context addendum: naming, undo, rollback, locking, no preparation ----

        [Fact]
        public async Task CopyExternal_UsesTheCopyLayoutName_AndTheSourcesWindowGeometry()
        {
            var rig = new Rig();
            await rig.RestoreExternalOnRx1Async();
            Assert.True(rig.Rx1Vm.IsExternalLayout);
            Assert.True(rig.Rx1Vm.CanCopyFrom);
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            rig.Client.MultiViewCalls.Clear();

            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm));

            string layout = $"avas_{PaxMac}_copy_3840x2160";
            Assert.Equal(new[] { $"layout:{layout}", $"multiview:{PaxMac}:{layout}:{Tx2Mac}@3,{VegaMac}@5" }, rig.Client.MultiViewCalls);
            Assert.Equal((3840, 2160), rig.Client.LastMultiViewSize);
            Assert.Equal(new (int, int, int, int, int, int, int, int?)[]
                {
                    (0, 0, 0, 1920, 1080, 0, 0, 3),
                    (1, 1920, 0, 1920, 1080, 16, 8, 5),
                    (2, 0, 1080, 1920, 1080, 0, 0, null), // no routed subscription: black
                },
                rig.Client.LastSurfacePlan!.Windows.Select(w =>
                    (w.Slot.Index, w.Slot.X, w.Slot.Y, w.Slot.Width, w.Slot.Height, w.HOffset, w.VOffset, w.Surface)));
            Assert.False(MultiViewPresets.TryParseName(layout, out _, out _, out _)); // restores as external
            Assert.Equal(new[] { 1, 0 }, rig.Client.LastSubscriptions!.Select(s => s.StreamIndex)); // the streams read back

            Assert.False(rig.MultiView.IsMultiView(rig.Pax)); // recorded like an adopted external layout (read-only)
            Assert.True(rig.PaxVm.IsExternalLayout);
            var now = rig.Vm.CurrentScreen(rig.PaxVm);
            Assert.Equal(ScreenKind.External, now.Kind);
            Assert.Equal(layout, now.External!.Name);
            Assert.Equal($"{PaxName} now shows {Rx1Name}'s External layout", rig.PaxVm.UndoToastText);

            // Off leaves what the copy joined.
            Assert.True(await rig.Vm.UnrouteVideoAsync(rig.Pax));
            Assert.Contains($"{PaxMac}:3", rig.Client.Leaves);
            Assert.Contains($"{PaxMac}:5", rig.Client.Leaves);
        }

        [Fact]
        public async Task Copy_OntoATargetInItsOwnMultiView_TouchesNoScalerOrDivider_AndLeavesWhatItNoLongerUses()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async(); // a 4K matrix: a Take would halve its sources
            await rig.RestoreSideBySideOnPaxAsync();
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            rig.Client.MultiViewCalls.Clear();

            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm));

            Assert.DoesNotContain(rig.Client.MultiViewCalls, c => c.StartsWith("scaler:") || c.StartsWith("divider:"));
            Assert.Equal(1, rig.Client.EdidReadsBy.Count(m => m == PaxMac)); // the arm's read only: no canvas re-pick
            Assert.Contains($"{PaxMac}:3", rig.Client.Leaves);
            Assert.Contains($"{PaxMac}:4", rig.Client.Leaves);
            Assert.Equal("matrix2x2", rig.MultiView.GetLive(rig.Pax)!.Plan.Id);
            Assert.Equal(ScreenKind.MultiView, rig.MultiView.PreviousScreen(rig.Pax)!.Kind);
            Assert.Equal("side_by_side", rig.MultiView.PreviousScreen(rig.Pax)!.Draft!.Plan.Id);

            // Undo takes the target's own picture again (its EDID canvas is right for it).
            Assert.True(await rig.Vm.UndoScreenAsync(rig.PaxVm));
            Assert.Equal("side_by_side", rig.MultiView.GetLive(rig.Pax)!.Plan.Id);
            Assert.Equal(new[] { $"0:{VegaMac}", $"1:{Tx2Mac}" }, Placed(rig.MultiView.GetLive(rig.Pax)));
            Assert.Contains($"layout:avas_{PaxMac}_side_by_side_3840x2160", rig.Client.MultiViewCalls);
            Assert.Equal("SIDE-BY-SIDE · 2/2", rig.PaxVm.ModeBadge);
        }

        [Fact]
        public async Task UndoScreen_OfATargetThatShowedNothing_LeavesEverythingAndSetsFastSwitch()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm));
            Assert.Equal(ScreenKind.Nothing, rig.MultiView.PreviousScreen(rig.Pax)!.Kind);
            rig.Client.MultiViewCalls.Clear();

            Assert.True(await rig.Vm.UndoScreenAsync(rig.PaxVm));

            Assert.Contains($"{PaxMac}:1", rig.Client.Leaves);
            Assert.Contains($"{PaxMac}:2", rig.Client.Leaves);
            Assert.Equal(new[] { $"fastswitch:{PaxMac}:3840x2160@60" }, rig.Client.MultiViewCalls);
            Assert.False(rig.MultiView.IsMultiView(rig.Pax));
            Assert.False(rig.PaxVm.IsMultiView);
            Assert.Equal("No source", rig.PaxVm.ModeBadge);
            Assert.False(rig.PaxVm.CanUndoScreen);
        }

        [Fact]
        public async Task Copy_FailedSetMultiView_RollsBack_AndSaysNothingChanged()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            await rig.Vm.RouteVideoAsync(rig.VegaVm.Device, rig.Pax);
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            rig.Client.LastVideoRx = rig.Client.LastVideoTx = null;
            rig.Client.FailMultiViewPrefix = "multiview:";

            Assert.False(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm));

            Assert.Equal($"'{PaxName}' rejected layout '{MatrixLayout(PaxMac)}'. Nothing changed on {PaxName}.", rig.PaxVm.LastError);
            Assert.Equal(VegaMac, rig.Client.LastVideoTx); // best-effort restore of what it showed
            Assert.False(rig.MultiView.IsMultiView(rig.Pax));
            Assert.Equal(VegaMac, rig.PaxVm.ActiveSourceId);
            Assert.Null(rig.PaxVm.UndoToastText);
            Assert.Null(rig.MultiView.PreviousScreen(rig.Pax));
        }

        [Fact]
        public void RouteToTheTarget_DuringACopy_WaitsForIt() => UiThread.Run(async () =>
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            rig.Client.MultiViewCalls.Clear();
            var (entered, release) = HoldFirstCall(rig.Client, "multiview:");

            var copy = rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm);
            await Within(entered, "Copy reaching set:multiview");
            var route = rig.Vm.RouteVideoAsync(rig.VegaVm.Device, rig.Pax);
            await Task.Delay(100);
            Assert.False(route.IsCompleted);
            Assert.Null(rig.Client.LastVideoTx);

            release();
            await Within(Task.WhenAll(copy, route), "Copy + Route");

            Assert.True(await copy);
            Assert.True(await route);
            var calls = rig.Client.MultiViewCalls;
            Assert.True(calls.FindIndex(c => c.StartsWith("multiview:")) < calls.IndexOf($"fastswitch:{PaxMac}:3840x2160@60"), string.Join(" | ", calls));
            Assert.Equal(VegaMac, rig.PaxVm.ActiveSourceId); // the Route ran last, and everybody agrees
            Assert.False(rig.MultiView.IsMultiView(rig.Pax));
            Assert.False(rig.PaxVm.IsMultiView);
        });

        [Fact]
        public async Task CopySingleView_OntoAMultiViewTarget_ReturnsItToFastSwitchFirst()
        {
            var rig = new Rig();
            await rig.RestoreSideBySideOnPaxAsync();
            await rig.Vm.RouteVideoAsync(rig.Tx2Vm.Device, rig.Rx1);
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            rig.Client.MultiViewCalls.Clear();

            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm));

            Assert.Equal(new[] { $"fastswitch:{PaxMac}:3840x2160@60" }, rig.Client.MultiViewCalls);
            Assert.Equal(PaxMac, rig.Client.LastVideoRx);
            Assert.Equal(Tx2Mac, rig.Client.LastVideoTx);
            Assert.False(rig.MultiView.IsMultiView(rig.Pax));
            Assert.False(rig.PaxVm.IsMultiView);
            Assert.Equal($"{PaxName} now shows {Rx1Name}'s Single View", rig.PaxVm.UndoToastText);
        }

        [Fact]
        public async Task Copy_WithTheComposerOpenOnTheTarget_KeepsItsDraft_AndShowsTheNotice()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            await rig.Vm.OpenComposerAsync(rig.PaxVm, "matrix2x2");
            var composer = rig.Vm.ActiveComposer!;
            composer.Arm(rig.VegaVm);
            composer.TapBlock(3);

            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm); // the Composer replaces the displays row: no arming
            Assert.Null(rig.Vm.ArmedDisplay);

            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm));

            Assert.Equal(new[] { $"3:{VegaMac}" }, Placed(composer.Draft));
            Assert.Equal(StaleNotice, composer.Banner.Text);
            Assert.Equal("matrix2x2", rig.MultiView.GetLive(rig.Pax)!.Plan.Id);
        }

        [Fact]
        public async Task ArmingADisplay_DisarmsAnArmedSource_AndViceVersa()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            rig.Vm.ArmSource(rig.Tx2Vm);

            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            Assert.Same(rig.Rx1Vm, rig.Vm.ArmedDisplay);
            Assert.Null(rig.Vm.ArmedSource);
            Assert.False(rig.Tx2Vm.IsArmed);

            rig.Vm.ArmSource(rig.VegaVm);
            Assert.Null(rig.Vm.ArmedDisplay);
            Assert.False(rig.Rx1Vm.IsCopySource);
            Assert.False(rig.PaxVm.IsCopyTargetAllowed);

            // The card's Copy layout handle toggles.
            rig.Rx1Vm.CopyLayoutCommand.Execute(null);
            await Within(WaitUntil(() => rig.Vm.ArmedDisplay != null && rig.PaxVm.IsCopyTargetAllowed), "Arm");
            rig.Rx1Vm.CopyLayoutCommand.Execute(null);
            await Within(WaitUntil(() => rig.Vm.ArmedDisplay == null), "Disarm");
            Assert.False(rig.PaxVm.IsCopyTargetAllowed);
        }

        [Fact]
        public async Task PreviousScreen_ExpiresOnRead_AndASecondCopyReplacesIt()
        {
            var client = new MockSdvoeClient();
            var mv = new MultiViewOrchestrator(client);
            var now = new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);
            mv.Clock = () => now;
            var target = new AvasDevice { DeviceId = PaxMac, DeviceName = PaxName, IsReceiver = true, IsActive = true };
            var single = new ScreenState(ScreenKind.Single, 1920, 1080, 60, Tx2Mac, Tx2Name, null, null, new List<RxSubscription>());

            var first = await mv.CopyAsync(target, single);
            Assert.True(first.Ok);
            Assert.Null(first.Error);
            Assert.Equal(ScreenKind.Nothing, mv.PreviousScreen(target)!.Kind);

            now += TimeSpan.FromSeconds(9.9);
            Assert.NotNull(mv.PreviousScreen(target));
            now += TimeSpan.FromSeconds(0.1);
            Assert.Null(mv.PreviousScreen(target)); // expired, evaluated on read

            var draft = new MultiViewDraft(MultiViewPresets.Build("matrix2x2", 1920, 1080));
            Assert.True(draft.Place(0, VegaMac, VegaName).Ok);
            var matrix = new ScreenState(ScreenKind.MultiView, 1920, 1080, 60, null, null, draft, null, new List<RxSubscription>());
            Assert.True((await mv.CopyAsync(target, matrix)).Ok);

            var previous = mv.PreviousScreen(target)!;
            Assert.Equal(ScreenKind.Single, previous.Kind); // the target's state at the second copy
            Assert.Equal(Tx2Mac, previous.SourceMac);
        }

        [Fact]
        public void Describe_SingleViewWithoutASourceName_HasNoEmptyParentheses()
        {
            var noName = new ScreenState(ScreenKind.Single, 1920, 1080, 60, Tx2Mac, string.Empty, null, null, new List<RxSubscription>());
            var nullName = noName with { SourceName = null };

            Assert.Equal("Single View - 1920x1080", noName.Describe());
            Assert.Equal("Single View - 1920x1080", nullName.Describe());
        }

        // ---- fix round 1 ----

        private static RxState Recognised(string mac, string name, string preset, int w, int h, params (int Window, int Sub, string Mac, string Name)[] blocks) =>
            new(mac, name, "MULTIVIEW", w, h, 60,
                blocks.Select(b => Sub(b.Sub, b.Mac, b.Name)).GroupBy(s => s.Index).Select(g => g.First()).ToList(),
                new RxLayout($"avas_{mac}_{preset}_{w}x{h}", w, h, blocks.Select(b => new RxWindow(b.Window, b.Sub)).ToList()));

        private static RxState External(string mac, string name, params RxSubscription[] subs) =>
            new(mac, name, "MULTIVIEW", 3840, 2160, 60, subs,
                new RxLayout("bavm_custom", 3840, 2160, subs.Where(s => s.Index != 0).Select((s, i) => new RxWindow(i, s.Index, i * 1920, 0, 1920, 1080)).ToList()));

        private static RxState Unrouted(string mac, string name) =>
            new(mac, name, "GENLOCKED", 0, 0, 0, Array.Empty<RxSubscription>(), null);

        [Theory]
        [InlineData("route")]
        [InlineData("off")]
        [InlineData("take")]
        [InlineData("restore")]
        public async Task AChangeToTheTargetWithinTheUndoWindow_WithdrawsTheCopysUndo(string change)
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm));
            Assert.True(rig.PaxVm.CanUndoScreen);
            rig.Now += TimeSpan.FromSeconds(4);

            switch (change)
            {
                case "route":
                    Assert.True(await rig.Vm.RouteVideoAsync(rig.VegaVm.Device, rig.Pax));
                    break;
                case "off":
                    Assert.True(await rig.Vm.UnrouteVideoAsync(rig.Pax));
                    break;
                case "take":
                    await rig.Vm.OpenComposerAsync(rig.PaxVm);
                    rig.Vm.ActiveComposer!.Arm(rig.VegaVm);
                    rig.Vm.ActiveComposer.TapBlock(3);
                    await rig.Vm.ActiveComposer.TakeAsync();
                    Assert.Equal(BannerKind.Success, rig.Vm.ActiveComposer.Banner.Kind);
                    break;
                default:
                    await rig.RestoreAsync(rig.PaxVm, Unrouted(PaxMac, PaxName)); // re-read from the hardware
                    break;
            }

            Assert.False(rig.PaxVm.CanUndoScreen);
            Assert.Null(rig.PaxVm.UndoToastText);
            Assert.Null(rig.MultiView.PreviousScreen(rig.Pax));
            rig.Client.MultiViewCalls.Clear();
            rig.Client.LastVideoRx = rig.Client.LastVideoTx = null;
            int leaves = rig.Client.Leaves.Count;

            Assert.False(await rig.Vm.UndoScreenAsync(rig.PaxVm)); // the newer deliberate change stays

            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.Null(rig.Client.LastVideoTx);
            Assert.Equal(leaves, rig.Client.Leaves.Count);
        }

        [Fact]
        public async Task ComposerTakeOnACopiedTarget_KeepsTheCopiedFormat()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async(1920, 1080);
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm)); // 1080p picture on a 4K display
            await rig.Vm.OpenComposerAsync(rig.PaxVm);
            var composer = rig.Vm.ActiveComposer!;
            composer.Arm(rig.VegaVm);
            composer.TapBlock(3);
            rig.Client.MultiViewCalls.Clear();

            await composer.TakeAsync();

            Assert.Equal(BannerKind.Success, composer.Banner.Kind); // Applied, not DisplayChanged
            string layout = MatrixLayout(PaxMac, 1920, 1080);
            Assert.Equal(new[] { $"scaler:{Tx2Mac}:960x540", $"scaler:{VegaMac}:960x540", $"layout:{layout}", $"multiview:{PaxMac}:{layout}:{Tx2Mac}@1,{VegaMac}@2" },
                rig.Client.MultiViewCalls); // a normal Take's scalers, at the one size each encoder already has
            Assert.Equal((1920, 1080), rig.Client.LastMultiViewSize);
            Assert.Equal((1920, 1080), (rig.MultiView.GetCanvas(rig.Pax)!.Width, rig.MultiView.GetCanvas(rig.Pax)!.Height));
            Assert.Equal(new[] { $"0:{Tx2Mac}", $"1:{VegaMac}", $"2:{Tx2Mac}", $"3:{VegaMac}" }, Placed(rig.MultiView.GetLive(rig.Pax)));
        }

        [Fact]
        public async Task TakeOnACopiedTarget_ThatNoLongerListsTheCopiedMode_IsRedrawnWithAnHonestReason()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async(1920, 1080);
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm));
            rig.Client.DisplayEdids[PaxMac] = TestEdids.Build((3840, 2160, 60, false)); // 4K only now
            var draft = rig.MultiView.GetLive(rig.Pax)!;
            Assert.True(draft.Place(3, VegaMac, VegaName).Ok);
            rig.Client.MultiViewCalls.Clear();

            var result = await rig.MultiView.TakeAsync(rig.Pax, draft);

            Assert.Equal(TakeOutcome.DisplayChanged, result.Outcome);
            Assert.Equal($"'{PaxName}' runs a copied 1920x1080 picture; the layout was redrawn for its own 3840x2160.", result.Error);
            Assert.Equal((3840, 2160), (result.NewPlan!.Width, result.NewPlan.Height));
            Assert.Empty(rig.Client.MultiViewCalls);
        }

        [Fact]
        public async Task UndoOfACopyOverACopy_PutsTheFirstCopyBackAtItsFormat()
        {
            var rig = new Rig(more: true);
            await rig.RestoreMatrixOnRx1Async(1920, 1080);
            await rig.RestoreAsync(rig.Rx3Vm, Recognised(Rx3Mac, "RX3", "side_by_side", 1920, 1080,
                (0, 3, VegaMac, VegaName), (1, 4, Tx2Mac, Tx2Name)));
            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm)); // 1080p matrix on the 4K PAX
            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx3Vm, rig.PaxVm)); // then RX3's side-by-side over it
            Assert.Equal("side_by_side", rig.MultiView.GetLive(rig.Pax)!.Plan.Id);
            rig.Client.MultiViewCalls.Clear();

            Assert.True(await rig.Vm.UndoScreenAsync(rig.PaxVm));

            Assert.Equal("matrix2x2", rig.MultiView.GetLive(rig.Pax)!.Plan.Id);
            Assert.Equal((1920, 1080), rig.Client.LastMultiViewSize);
            Assert.Contains($"layout:{MatrixLayout(PaxMac, 1920, 1080)}", rig.Client.MultiViewCalls);
            Assert.Equal("MATRIX 2X2 · 3/4", rig.PaxVm.ModeBadge);
        }

        [Fact]
        public async Task RouteFromAMultiView_LeavesItsStreamOneJoins_NeverIndexZero()
        {
            var rig = new Rig();
            await rig.RestoreSideBySideOnPaxAsync(); // joined 3 and 4

            Assert.True(await rig.Vm.RouteVideoAsync(rig.Tx2Vm.Device, rig.Pax));

            Assert.Contains($"{PaxMac}:3", rig.Client.Leaves);
            Assert.Contains($"{PaxMac}:4", rig.Client.Leaves);
            Assert.DoesNotContain($"{PaxMac}:0", rig.Client.Leaves);
            Assert.Equal(Tx2Mac, rig.Client.LastVideoTx);
        }

        [Fact]
        public async Task RouteOntoAnExternalLayout_LeavesItsSubscriptionsExceptIndexZero()
        {
            var rig = new Rig();
            await rig.RestoreAsync(rig.PaxVm, External(PaxMac, PaxName, Sub(0, VegaMac, VegaName), Sub(3, Tx2Mac, Tx2Name), Sub(5, VegaMac, VegaName)));
            Assert.True(rig.PaxVm.IsExternalLayout);

            Assert.True(await rig.Vm.RouteVideoAsync(rig.Tx2Vm.Device, rig.Pax));

            Assert.Equal(new[] { $"{PaxMac}:3", $"{PaxMac}:5" }, rig.Client.Leaves);
            Assert.Equal(Tx2Mac, rig.Client.LastVideoTx);
        }

        [Fact]
        public async Task SingleCopyOntoAnExternalLayout_LeavesItsSubscriptionsExceptIndexZero()
        {
            var rig = new Rig();
            await rig.RestoreAsync(rig.PaxVm, External(PaxMac, PaxName, Sub(0, VegaMac, VegaName), Sub(3, VegaMac, VegaName)));
            await rig.Vm.RouteVideoAsync(rig.Tx2Vm.Device, rig.Rx1);
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);

            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm));

            Assert.Equal(new[] { $"{PaxMac}:3" }, rig.Client.Leaves);
            Assert.Equal(Tx2Mac, rig.Client.LastVideoTx);
            Assert.False(rig.PaxVm.IsExternalLayout);
        }

        [Fact]
        public async Task UndoBackToSingleView_FromACopiedExternalLayout_LeavesTheCopysJoins()
        {
            var rig = new Rig();
            await rig.RestoreExternalOnRx1Async();
            await rig.Vm.RouteVideoAsync(rig.VegaVm.Device, rig.Pax);
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm)); // joined 3 and 5 on PAX
            rig.Client.Leaves.Clear();

            Assert.True(await rig.Vm.UndoScreenAsync(rig.PaxVm));

            Assert.Equal(new[] { $"{PaxMac}:3", $"{PaxMac}:5" }, rig.Client.Leaves);
            Assert.Equal(VegaMac, rig.Client.LastVideoTx);
            Assert.Equal(VegaMac, rig.PaxVm.ActiveSourceId);
        }

        [Fact]
        public async Task Copy_WhenTheRollbackAlsoFails_SaysSo_AndReReadsTheTarget()
        {
            var rig = new Rig();
            await rig.RestoreSideBySideOnPaxAsync();
            await rig.Vm.RouteVideoAsync(rig.Tx2Vm.Device, rig.Rx1);
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            rig.Client.FailJoin = true;                   // the Single View route is refused after the genlock...
            rig.Client.FailMultiViewPrefix = "multiview:"; // ...and putting the side-by-side back is refused too
            rig.Client.ReceiverStates[PaxMac] = Unrouted(PaxMac, PaxName); // what the decoder really shows now
            rig.Client.ReceiverReads.Clear();

            Assert.False(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm));

            Assert.Equal($"'{PaxName}' did not accept the route to '{Tx2Name}'. {PaxName} could not be put back.", rig.PaxVm.LastError);
            Assert.Contains(PaxMac, rig.Client.ReceiverReads); // re-read: the card shows the truth
            Assert.False(rig.PaxVm.IsMultiView);
            Assert.Equal("No source", rig.PaxVm.ModeBadge);
        }

        [Fact]
        public async Task CopyOfAPictureThatConflictsWithAThirdDisplay_IsRefused_BeforeAnyCall()
        {
            var rig = new Rig(more: true);
            await rig.RestoreMatrixOnRx1Async(); // TX2 at 1920x1080 on RX1
            await rig.RestoreAsync(rig.Rx3Vm, Recognised(Rx3Mac, "RX3", "matrix2x2", 1920, 1080, (0, 1, Tx2Mac, Tx2Name))); // TX2 at 960x540
            rig.Client.MultiViewCalls.Clear();

            Assert.False(await rig.Vm.CopyDisplayAsync(rig.Rx3Vm, rig.PaxVm));

            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.False(rig.MultiView.IsMultiView(rig.Pax));
            Assert.Equal($"{Tx2Name} runs at 1920x1080 on {Rx1Name} - this block is 960x540. Nothing changed on {PaxName}.", rig.PaxVm.LastError);
        }

        [Fact]
        public async Task TapOrDropWhileTheArmIsStillReading_IsIgnored()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Client.DuringEdidRead = () => gate.Task;
            rig.Client.MultiViewCalls.Clear();

            var arm = rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            // Bounded: a regression that waits on the held EDID read must fail here, not hang the suite.
            await Within(rig.Vm.TapDisplayAsync(rig.PaxVm), "Tap while reading");
            var drop = rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm);
            await Within(drop, "Drop while reading");
            bool dropped = await drop;

            Assert.False(dropped);
            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.False(rig.PaxVm.IsCopyTargetBlocked); // no false "Can't read" block
            Assert.Equal(string.Empty, rig.PaxVm.CopyTargetText);

            gate.SetResult();
            await Within(arm, "Arm");
            Assert.True(rig.PaxVm.IsCopyTargetAllowed);
            Assert.Same(rig.Rx1Vm, rig.Vm.ArmedDisplay);
        }

        // ---- fix round 2 ----

        [Fact]
        public async Task UndoQueuedBehindARoute_DoesNothing_WhenTheRouteWithdrewTheSnapshot()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            Assert.True(await rig.Vm.CopyDisplayAsync(rig.Rx1Vm, rig.PaxVm)); // PAX showed nothing before
            Assert.NotNull(rig.MultiView.PreviousScreen(rig.Pax));
            rig.Client.MultiViewCalls.Clear();
            rig.Client.Leaves.Clear();

            // Something already holds PAX: the Route queues first, the Undo click after it.
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var holder = rig.MultiView.RunExclusiveAsync(rig.Pax, () => gate.Task);
            var route = rig.Vm.RouteVideoAsync(rig.VegaVm.Device, rig.Pax);
            var undo = rig.Vm.UndoScreenAsync(rig.PaxVm); // read the snapshot before the Route could withdraw it
            Assert.False(route.IsCompleted);
            Assert.False(undo.IsCompleted);

            gate.SetResult(true);
            await Within(Task.WhenAll(holder, route, undo), "Holder + Route + Undo");

            Assert.True(await route);
            Assert.False(await undo); // the Route withdrew the snapshot: the late Undo must not overwrite it
            Assert.Equal(new[] { $"fastswitch:{PaxMac}:3840x2160@60" }, rig.Client.MultiViewCalls); // the Route's only
            Assert.Equal(VegaMac, rig.Client.LastVideoTx);
            Assert.Equal(VegaMac, rig.PaxVm.ActiveSourceId);
            Assert.DoesNotContain($"{PaxMac}:0", rig.Client.Leaves);
        }

        [Fact]
        public async Task ReturnToSingleView_WhoseFastSwitchFails_ForgetsTheSubscriptionsItAlreadyLeft()
        {
            var rig = new Rig();
            await rig.RestoreSideBySideOnPaxAsync(); // joined 3 and 4
            rig.Client.FailMultiViewPrefix = "fastswitch:";

            Assert.False(await rig.Vm.RouteVideoAsync(rig.Tx2Vm.Device, rig.Pax));
            Assert.Equal(new[] { $"{PaxMac}:3", $"{PaxMac}:4" }, rig.Client.Leaves); // the leaves did happen
            Assert.True(rig.MultiView.IsMultiView(rig.Pax)); // still in multiview: the genlock was refused

            rig.Client.FailMultiViewPrefix = null;
            rig.Client.Leaves.Clear();
            Assert.True(await rig.Vm.UnrouteVideoAsync(rig.Pax));

            Assert.Empty(rig.Client.Leaves); // nothing left to leave: 3 and 4 are no longer counted as joined
            Assert.Equal($"fastswitch:{PaxMac}:3840x2160@60", rig.Client.MultiViewCalls.Last());
        }

        // ---- Task 10b fix round 1: the overview's focus on the displays, the picture tap, the handle toggle ----

        [Fact]
        public async Task CopyOrChoiceActive_FollowsTheCopyArmAndTheDropChoice()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            var raised = new List<string?>();
            rig.Vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            Assert.False(rig.Vm.IsCopyOrChoiceActive);

            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            Assert.True(rig.Vm.IsCopyOrChoiceActive);
            Assert.Contains(nameof(MainViewModel.IsCopyOrChoiceActive), raised);
            rig.Vm.DisarmDisplay();
            Assert.False(rig.Vm.IsCopyOrChoiceActive);

            raised.Clear();
            rig.Vm.OpenDropChoice(rig.Rx1Vm, rig.Tx2Vm);
            Assert.True(rig.Vm.IsCopyOrChoiceActive);
            Assert.Contains(nameof(MainViewModel.IsCopyOrChoiceActive), raised);

            rig.Now += MainViewModel.DropChoiceTimeout + TimeSpan.FromSeconds(1);
            rig.Vm.Tick(); // the choice closes by itself
            Assert.False(rig.Rx1Vm.IsDropChoiceOpen);
            Assert.False(rig.Vm.IsCopyOrChoiceActive);

            rig.Vm.OpenDropChoice(rig.Rx1Vm, rig.Tx2Vm);
            await rig.Vm.ChooseIntoBlockAsync(rig.Rx1Vm);
            Assert.False(rig.Vm.IsCopyOrChoiceActive);
        }

        [Fact]
        public async Task TapPicture_OnAMultiViewCard_OpensTheComposer_ButNotWhileACopyIsArmedOrAChoiceIsOpen()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();

            // A Single View / unrouted card: the picture tap is a plain card tap (nothing armed: nothing happens).
            await rig.Vm.TapPictureAsync(rig.PaxVm);
            Assert.Null(rig.Vm.ActiveComposer);

            // A copy armed: the tap is a copy-target tap (on the source itself: nothing), never the Composer.
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);
            await rig.Vm.TapPictureAsync(rig.Rx1Vm);
            Assert.Null(rig.Vm.ActiveComposer);
            Assert.Same(rig.Rx1Vm, rig.Vm.ArmedDisplay);
            rig.Vm.DisarmDisplay();

            // A drop choice open: the choice decides, not the picture.
            rig.Vm.OpenDropChoice(rig.Rx1Vm, rig.Tx2Vm);
            await rig.Vm.TapPictureAsync(rig.Rx1Vm);
            Assert.Null(rig.Vm.ActiveComposer);
            Assert.True(rig.Rx1Vm.IsDropChoiceOpen);
            await rig.Vm.ChooseIntoBlockAsync(rig.Rx1Vm);
            rig.Vm.CloseComposer();

            // Nothing armed, a MultiView card: the Composer opens on what is on screen.
            await rig.Vm.TapPictureAsync(rig.Rx1Vm);
            Assert.NotNull(rig.Vm.ActiveComposer);
            Assert.Equal(Placed(rig.MultiView.GetLive(rig.Rx1)), Placed(rig.Vm.ActiveComposer!.Draft));
        }

        [Fact]
        public async Task CopyArm_DisarmsAfterTheIdleTimeout_ButATargetCheckCountsAsActivity()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);

            rig.Now += MainViewModel.ArmIdleTimeout - TimeSpan.FromSeconds(1);
            rig.Vm.Tick();
            Assert.Same(rig.Rx1Vm, rig.Vm.ArmedDisplay);

            // A drag over a card (or a tap on one) asks for its decision: that is activity.
            rig.Vm.CopyDecisionFor(rig.PaxVm);
            rig.Now += TimeSpan.FromSeconds(2);
            rig.Vm.Tick();
            Assert.Same(rig.Rx1Vm, rig.Vm.ArmedDisplay);

            rig.Now += MainViewModel.ArmIdleTimeout;
            rig.Vm.Tick();
            Assert.Null(rig.Vm.ArmedDisplay);
            Assert.Null(rig.Vm.CopyArmBanner);
            Assert.False(rig.Vm.IsCopyOrChoiceActive);
            Assert.False(rig.PaxVm.IsCopyTargetAllowed);
            Assert.False(rig.Rx1Vm.IsCopySource);
        }

        [Fact]
        public async Task ToggleCopyArm_ArmsThenStops()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            await rig.Vm.ToggleCopyArmAsync(rig.Rx1Vm);
            Assert.Same(rig.Rx1Vm, rig.Vm.ArmedDisplay);
            await rig.Vm.ToggleCopyArmAsync(rig.Rx1Vm);
            Assert.Null(rig.Vm.ArmedDisplay);
        }

        /// <summary>Polls a view-model condition set by a fire-and-forget command.</summary>
        private static async Task WaitUntil(Func<bool> condition)
        {
            while (!condition()) await Task.Delay(10);
        }

        // ---- Task 11: keyboard path, automation names, reduced motion ----

        [Fact]
        public async Task Keyboard_CopyLayout_EnterArms_EnterOnTargetCopies_EscStops()
        {
            var rig = new Rig(more: true);
            await rig.RestoreMatrixOnRx1Async();

            // Enter (or Space) on the focused Copy layout handle is its click: the toggle the tap uses.
            await rig.Vm.ToggleCopyArmAsync(rig.Rx1Vm);
            Assert.Same(rig.Rx1Vm, rig.Vm.ArmedDisplay);
            Assert.True(rig.PaxVm.IsCopyTargetAllowed);
            rig.Client.MultiViewCalls.Clear();

            // Esc stops: nothing was sent, every card is a plain card again.
            Assert.True(rig.Vm.Escape());
            Assert.Null(rig.Vm.ArmedDisplay);
            Assert.False(rig.Rx1Vm.IsCopySource);
            Assert.All(rig.Vm.Displays, d => Assert.False(d.IsCopyTargetAllowed || d.IsCopyTargetBlocked));
            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.False(rig.Vm.Escape()); // nothing armed: nothing to stop

            // Arm again, Tab to the target card, Enter on its picture copies - the entry a tap uses.
            await rig.Vm.ToggleCopyArmAsync(rig.Rx1Vm);
            await rig.Vm.TapPictureAsync(rig.PaxVm);

            Assert.Contains($"multiview:{PaxMac}:{MatrixLayout(PaxMac)}:{Tx2Mac}@1,{VegaMac}@2", rig.Client.MultiViewCalls);
            Assert.Null(rig.Vm.ArmedDisplay);
            Assert.True(rig.PaxVm.CanUndoScreen);
        }

        [Fact]
        public async Task Keyboard_EnterOnABlockedCopyTarget_DoesNothing_AndKeepsItsReason()
        {
            var rig = new Rig(more: true);
            await rig.RestoreMatrixOnRx1Async();
            await rig.Vm.ToggleCopyArmAsync(rig.Rx1Vm);
            rig.Client.MultiViewCalls.Clear();

            await rig.Vm.TapPictureAsync(rig.Rx3Vm); // Enter on RX3, which is only 1920x1080
            await rig.Vm.TapPictureAsync(rig.Rx1Vm); // and on the source itself

            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.Same(rig.Rx1Vm, rig.Vm.ArmedDisplay); // still armed: Tab on to a target that works
            Assert.True(rig.Rx3Vm.IsCopyTargetBlocked);
            Assert.Contains("RX3 is 1920x1080", rig.Rx3Vm.PictureAutomationName); // the reason is what a screen reader hears
        }

        [Fact]
        public async Task CopyTargets_AnnounceAllowedOrBlocked()
        {
            var rig = new Rig(more: true);
            await rig.RestoreMatrixOnRx1Async();
            rig.Rx4Vm.SetOnlineState(false);
            Assert.Equal($"{Rx1Name}, picture on screen, MATRIX 2X2 · 3/4", rig.Rx1Vm.PictureAutomationName);
            Assert.Equal("RX3, nothing on screen", rig.Rx3Vm.PictureAutomationName);

            var raised = new List<string?>();
            rig.PaxVm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            await rig.Vm.ArmDisplayAsync(rig.Rx1Vm);

            // Allowed: the target says what will be copied (CopyTargetText); blocked: the reason.
            Assert.NotEqual(string.Empty, rig.PaxVm.CopyTargetText);
            Assert.Contains(rig.PaxVm.CopyTargetText, rig.PaxVm.PictureAutomationName);
            Assert.StartsWith($"{PaxName}, copy target", rig.PaxVm.PictureAutomationName);
            Assert.Contains(nameof(DisplayCardViewModel.PictureAutomationName), raised);

            Assert.Contains(rig.Rx3Vm.CopyTargetText, rig.Rx3Vm.PictureAutomationName);
            Assert.StartsWith("RX3, cannot copy here", rig.Rx3Vm.PictureAutomationName);
            Assert.Contains("RX4 is offline", rig.Rx4Vm.PictureAutomationName);

            // The source itself says so.
            Assert.Equal($"{Rx1Name}, copying from here", rig.Rx1Vm.PictureAutomationName);

            rig.Vm.DisarmDisplay();
            Assert.DoesNotContain("copy target", rig.PaxVm.PictureAutomationName);
            Assert.Equal($"{PaxName}, nothing on screen", rig.PaxVm.PictureAutomationName);
        }

        [Fact]
        public async Task CopyHandle_AutomationName_SaysWhatItCopies_OrWhyItCannot()
        {
            var rig = new Rig();
            Assert.Equal($"Copy layout from {Rx1Name}. {Rx1Name} shows nothing to copy", rig.Rx1Vm.CopyHandleAutomationName);

            await rig.RestoreMatrixOnRx1Async();
            Assert.Equal($"Copy layout from {Rx1Name}", rig.Rx1Vm.CopyHandleAutomationName);

            await rig.Vm.ToggleCopyArmAsync(rig.Rx1Vm);
            Assert.Equal($"Copy layout from {Rx1Name}, armed. Press again to stop.", rig.Rx1Vm.CopyHandleAutomationName);
        }

        [Fact]
        public async Task Escape_InTheOverview_StopsWhatIsArmed_OneThingAtATime()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            Assert.False(rig.Vm.Escape()); // nothing armed

            rig.Vm.ArmSource(rig.Tx2Vm);
            Assert.True(rig.Vm.Escape());
            Assert.Null(rig.Vm.ArmedSource);

            rig.Vm.OpenDropChoice(rig.Rx1Vm, rig.Tx2Vm);
            Assert.True(rig.Vm.Escape());
            Assert.False(rig.Rx1Vm.IsDropChoiceOpen);
            Assert.False(rig.Vm.Escape());
        }

        [Fact]
        public async Task Escape_WithTheComposerOpen_IsTheComposersLadder_AndNeverClosesIt()
        {
            var rig = new Rig();
            await rig.RestoreMatrixOnRx1Async();
            await rig.Vm.OpenComposerAsync(rig.Rx1Vm);
            var composer = rig.Vm.ActiveComposer!;
            composer.Arm(rig.Tx2Vm);

            Assert.True(rig.Vm.Escape());
            Assert.Null(composer.ArmedSource);
            Assert.Null(rig.Vm.ArmedSource);

            Assert.False(rig.Vm.Escape()); // a second Esc does nothing
            Assert.Same(composer, rig.Vm.ActiveComposer); // and the Composer stays open
        }

        [Fact]
        public async Task Transitions_FollowTheWindowsSetting_InTheOverviewAndItsComposers()
        {
            bool animations = false;
            var client = new MockSdvoeClient();
            var vm = new MainViewModel(new ConfigService(), client, new MockRtpReceiver(), new MulticastIpManager(),
                new RoutingOrchestrator(client), new MultiViewOrchestrator(client), animationsEnabled: () => animations);
            var rx = new DisplayCardViewModel(new AvasDevice { DeviceId = Rx1Mac, DeviceName = Rx1Name, IsReceiver = true, IsActive = true }, vm.UnrouteVideoAsync);
            vm.Displays.Add(rx);

            Assert.False(vm.AnimationsEnabled);
            Assert.Equal(TimeSpan.Zero, vm.FadeDuration);

            await vm.OpenComposerAsync(rx, "matrix2x2");
            Assert.False(vm.ActiveComposer!.AnimationsEnabled);
            Assert.Equal(TimeSpan.Zero, vm.ActiveComposer.FrameTransitionDuration);

            animations = true; // read when needed, not captured
            Assert.True(vm.AnimationsEnabled);
            Assert.Equal(TimeSpan.FromMilliseconds(150), vm.FadeDuration);
            Assert.True(vm.ActiveComposer.AnimationsEnabled);
            Assert.Equal(TimeSpan.FromMilliseconds(400), vm.ActiveComposer.FrameTransitionDuration);
        }
    }
}
