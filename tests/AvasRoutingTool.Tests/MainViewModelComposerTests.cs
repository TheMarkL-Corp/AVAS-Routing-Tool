using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using AvasRoutingTool.ViewModels;
using Xunit;

// NOTE: test names in this file must not contain the substring "Live": the shared filter FullyQualifiedName!~Live
// (which keeps hardware tests out of the normal run) would silently skip them.
namespace AvasRoutingTool.Tests
{
    /// <summary>The overview and the Composer together: tap-to-arm, the drop choice, drafts on cards, restore while composing.</summary>
    public class MainViewModelComposerTests
    {
        private sealed class Rig
        {
            public MockSdvoeClient Client { get; } = new();
            public MultiViewOrchestrator MultiView { get; }
            public MainViewModel Vm { get; }
            public AvasDevice Rx { get; } = new() { DeviceId = "rx1", DeviceName = "RX1", IsReceiver = true, IsActive = true };
            public AvasDevice Rx2 { get; } = new() { DeviceId = "rx2", DeviceName = "RX2", IsReceiver = true, IsActive = true };
            public DisplayCardViewModel RxVm { get; }
            public DisplayCardViewModel Rx2Vm { get; }
            public SourceCardViewModel Tx1Vm { get; } = Source("tx1", "TX1");
            public SourceCardViewModel Tx2Vm { get; } = Source("tx2", "TX2");
            public DateTime Now { get; set; } = new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);

            /// <param name="post">The UI-thread poster for device events; null = the default (inline when there is no dispatcher).</param>
            public Rig(Action<Action>? post = null)
            {
                MultiView = new MultiViewOrchestrator(Client);
                Vm = new MainViewModel(new ConfigService(), Client, new MockRtpReceiver(), new MulticastIpManager(),
                    new RoutingOrchestrator(Client), MultiView, () => Now, post);
                RxVm = new DisplayCardViewModel(Rx, Vm.UnrouteVideoAsync);
                Rx2Vm = new DisplayCardViewModel(Rx2, Vm.UnrouteVideoAsync);
                Vm.Displays.Add(RxVm);
                Vm.Displays.Add(Rx2Vm);
                Vm.Sources.Add(Tx1Vm);
                Vm.Sources.Add(Tx2Vm);
            }

            public MultiViewComposerViewModel Composer => Vm.ActiveComposer!;

            /// <summary>Puts a matrix with TX1 in block 1 on RX1's screen through the Composer, then closes it.</summary>
            public async Task TakeMatrixAsync()
            {
                await Vm.OpenComposerAsync(RxVm, "matrix2x2");
                Composer.Arm(Tx1Vm);
                Composer.TapBlock(0);
                await Composer.TakeAsync();
                Vm.CloseComposer();
                Assert.True(RxVm.IsMultiView);
                Client.MultiViewCalls.Clear();
            }

            private static SourceCardViewModel Source(string mac, string name) =>
                new(new AvasDevice { DeviceId = mac, DeviceName = name, VendorId = 105, IsTransmitter = true, IsActive = true });
        }

        private static string[] Placed(MultiViewDraft? draft) =>
            draft == null
                ? Array.Empty<string>()
                : draft.Placements.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.SourceMac}").ToArray();

        /// <summary>A multiview state as the decoder reports it, for a layout this tool created (legacy name).</summary>
        private static RxState MatrixState(string rxMac, params (int Window, int Sub, string Mac, string Name)[] blocks) =>
            new(rxMac, rxMac.ToUpperInvariant(), "MULTIVIEW", 1920, 1080, 60,
                blocks.Select(b => new RxSubscription(b.Sub, true, "STREAMING", b.Mac, b.Name, 1)).ToList(),
                new RxLayout("avas_matrix2x2_1920x1080", 1920, 1080, blocks.Select(b => new RxWindow(b.Window, b.Sub)).ToList()));

        private static RxState SingleState(string rxMac, string txMac, string txName) =>
            new(rxMac, rxMac.ToUpperInvariant(), "GENLOCK", 1920, 1080, 60,
                new[] { new RxSubscription(0, true, "STREAMING", txMac, txName, 0) }, null);

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

        // ---- the listed tests ----

        [Fact]
        public async Task ArmThenTapDisplay_RoutesSingleViewImmediately_AndDisarms()
        {
            var rig = new Rig();

            rig.Vm.ArmSource(rig.Tx1Vm);
            Assert.Same(rig.Tx1Vm, rig.Vm.ArmedSource);
            Assert.True(rig.Tx1Vm.IsArmed);

            await rig.Vm.TapDisplayAsync(rig.RxVm);

            Assert.Equal("tx1", rig.Client.LastVideoTx);
            Assert.Equal("tx1", rig.RxVm.ActiveSourceId);
            Assert.Equal("SINGLE VIEW", rig.RxVm.ModeBadge);
            Assert.Null(rig.Vm.ArmedSource);
            Assert.False(rig.Tx1Vm.IsArmed);
        }

        [Fact]
        public async Task TapMultiViewCard_WithArmedSource_OpensTheDropChoice_NotARoute()
        {
            var rig = new Rig();
            await rig.TakeMatrixAsync();
            rig.Vm.ArmSource(rig.Tx2Vm);

            await rig.Vm.TapDisplayAsync(rig.RxVm);

            Assert.True(rig.RxVm.IsDropChoiceOpen);
            Assert.Same(rig.Tx2Vm, rig.RxVm.DropChoiceSource);
            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.Null(rig.Client.LastVideoTx);
            Assert.True(rig.RxVm.IsMultiView);
            Assert.True(rig.MultiView.IsMultiView(rig.Rx));
        }

        [Fact]
        public async Task DropChoice_FullScreen_LeavesMultiViewAndRoutes()
        {
            var rig = new Rig();
            await rig.TakeMatrixAsync();
            rig.Vm.ArmSource(rig.Tx2Vm);
            await rig.Vm.TapDisplayAsync(rig.RxVm);

            await rig.Vm.ChooseFullScreenAsync(rig.RxVm);

            Assert.Equal(new[] { "fastswitch:rx1:1920x1080@60" }, rig.Client.MultiViewCalls);
            Assert.Equal("tx2", rig.Client.LastVideoTx);
            Assert.False(rig.RxVm.IsMultiView);
            Assert.Empty(rig.RxVm.Slots);
            Assert.Equal("tx2", rig.RxVm.ActiveSourceId);
            Assert.False(rig.RxVm.IsDropChoiceOpen);
            Assert.Null(rig.RxVm.DropChoiceSource);
            Assert.Null(rig.Vm.ArmedSource); // one Single View route disarms
            Assert.False(rig.Tx2Vm.IsArmed);
        }

        [Fact]
        public async Task DropChoice_IntoBlock_OpensTheComposerWithTheSourceArmed()
        {
            var rig = new Rig();
            await rig.TakeMatrixAsync();
            rig.Vm.ArmSource(rig.Tx2Vm);
            await rig.Vm.TapDisplayAsync(rig.RxVm);

            await rig.Vm.ChooseIntoBlockAsync(rig.RxVm);

            Assert.False(rig.RxVm.IsDropChoiceOpen);
            Assert.NotNull(rig.Vm.ActiveComposer);
            Assert.Same(rig.Tx2Vm, rig.Composer.ArmedSource);
            Assert.Same(rig.Tx2Vm, rig.Vm.ArmedSource);
            Assert.True(rig.Tx2Vm.IsArmed);
            Assert.Equal(new[] { "0:tx1" }, Placed(rig.Composer.Draft)); // starts from what is on screen
            Assert.Empty(rig.Client.MultiViewCalls);

            rig.Composer.TapBlock(1);
            rig.Composer.TapBlock(2);
            Assert.Equal(2, rig.Tx2Vm.UsedInBlocks);
            Assert.Equal(1, rig.Tx1Vm.UsedInBlocks);
            Assert.Empty(rig.Client.MultiViewCalls);
        }

        [Fact]
        public async Task LayoutTileDrop_OpensTheComposerWithThatLayoutStaged()
        {
            var rig = new Rig();

            await rig.Vm.OpenComposerAsync(rig.RxVm, "pip");

            Assert.NotNull(rig.Vm.ActiveComposer);
            Assert.Equal("pip", rig.Composer.SelectedLayoutId);
            Assert.True(rig.Composer.StepLayoutDone);
            Assert.Equal(2, rig.Composer.Blocks.Count);
            Assert.All(rig.Composer.Blocks, b => Assert.False(b.IsPlaced));
            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.False(rig.RxVm.IsMultiView); // the card keeps showing the screen
        }

        [Fact]
        public async Task CardLayoutMenu_Preset_OpensTheComposerWithThatLayoutStaged_NoDeviceChange()
        {
            var rig = new Rig();
            await rig.Vm.RouteVideoAsync(rig.Tx1Vm.Device, rig.Rx);
            Assert.Equal(new[] { "single", "matrix2x2", "pip", "pip_large", "side_by_side", "quad_1_3", "off" },
                rig.RxVm.LayoutMenu.Select(l => l.Id));
            Assert.Equal("Matrix 2x2", rig.RxVm.LayoutMenu[1].Title);
            rig.RxVm.IsLayoutMenuOpen = true;
            rig.Client.MultiViewCalls.Clear(); // the route above set Fast Switch; choosing a preset must send nothing more

            bool ok = await rig.Vm.ChooseLayoutFromCardAsync(rig.RxVm, "matrix2x2");

            Assert.True(ok);
            Assert.False(rig.RxVm.IsLayoutMenuOpen);
            Assert.Equal("matrix2x2", rig.Composer.SelectedLayoutId);
            Assert.Equal(4, rig.Composer.Blocks.Count);
            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.Null(rig.Client.LastUnroutedRx);
            Assert.Equal("tx1", rig.RxVm.ActiveSourceId); // still in Single View until Take
            Assert.Equal("SINGLE VIEW", rig.RxVm.ModeBadge);
        }

        [Fact]
        public async Task CardLayoutMenu_SingleOrOff_BehaveLikeTheBottomBarTiles()
        {
            var rig = new Rig();
            await rig.TakeMatrixAsync();
            rig.RxVm.IsLayoutMenuOpen = true;

            Assert.True(await rig.Vm.ChooseLayoutFromCardAsync(rig.RxVm, "single"));

            Assert.Equal(new[] { "fastswitch:rx1:1920x1080@60" }, rig.Client.MultiViewCalls);
            Assert.False(rig.RxVm.IsMultiView);
            Assert.False(rig.MultiView.IsMultiView(rig.Rx));
            Assert.False(rig.RxVm.IsLayoutMenuOpen);
            Assert.Null(rig.Vm.ActiveComposer);

            await rig.Vm.RouteVideoAsync(rig.Tx2Vm.Device, rig.Rx);
            rig.RxVm.IsLayoutMenuOpen = true;

            Assert.True(await rig.Vm.ChooseLayoutFromCardAsync(rig.RxVm, "off"));

            Assert.Equal("rx1", rig.Client.LastUnroutedRx);
            Assert.False(rig.RxVm.IsRouted);
            Assert.Equal("UNROUTED", rig.RxVm.ModeBadge);
            Assert.False(rig.RxVm.IsLayoutMenuOpen);
        }

        [Fact]
        public void ModeBadge_ShowsLayoutAndFill_FromTheOnScreenState()
        {
            var card = new DisplayCardViewModel(new AvasDevice { DeviceId = "rx1", DeviceName = "RX1", IsReceiver = true, IsActive = true });
            card.ApplyLayout(MultiViewPresets.Build("matrix2x2", 1920, 1080), "Matrix 2x2");
            for (int i = 0; i < 3; i++)
                card.AssignSlot(i, new SourceCardViewModel(new AvasDevice { DeviceId = $"tx{i}", DeviceName = $"TX{i}", IsTransmitter = true, IsActive = true }));

            Assert.Equal("MATRIX 2X2 · 3/4", card.ModeBadge);
        }

        [Fact]
        public async Task CloseComposer_WithChanges_KeepsADraft_AndTheCardShowsIt()
        {
            var rig = new Rig();
            await rig.Vm.OpenComposerAsync(rig.RxVm, "matrix2x2");
            rig.Vm.ArmSource(rig.Tx1Vm);
            rig.Composer.TapBlock(0);
            rig.Composer.TapBlock(3);

            rig.Vm.CloseComposer();

            Assert.Null(rig.Vm.ActiveComposer);
            Assert.True(rig.RxVm.HasDraft);
            Assert.False(rig.RxVm.IsMultiView); // nothing went on screen
            Assert.Empty(rig.Client.MultiViewCalls);
            Assert.False(rig.Tx1Vm.IsArmed);
            Assert.Null(rig.Vm.ArmedSource);
            Assert.Equal(0, rig.Tx1Vm.UsedInBlocks); // composer only

            await rig.Vm.OpenComposerAsync(rig.RxVm);

            Assert.Equal("matrix2x2", rig.Composer.SelectedLayoutId);
            Assert.Equal(new[] { "0:tx1", "3:tx1" }, Placed(rig.Composer.Draft));
            Assert.Equal(2, rig.Tx1Vm.UsedInBlocks);
        }

        [Fact]
        public async Task RestoreWhileComposing_KeepsTheDraft_UpdatesTheScreenState_ShowsNotice()
        {
            var rig = new Rig();
            rig.Client.ReceiverStates["rx1"] = MatrixState("rx1", (0, 1, "tx1", "TX1"));
            await rig.Vm.RestoreStateAsync();
            await rig.Vm.OpenComposerAsync(rig.RxVm);
            rig.Composer.Arm(rig.Tx2Vm);
            rig.Composer.TapBlock(1); // a staged change
            rig.Client.ReceiverStates["rx1"] = MatrixState("rx1", (0, 1, "tx1", "TX1"), (3, 2, "tx2", "TX2")); // changed elsewhere

            await rig.Vm.RestoreDisplayAsync(rig.RxVm);

            Assert.Equal(new[] { "0:tx1", "1:tx2" }, Placed(rig.Composer.Draft)); // the draft is kept
            Assert.Equal(new[] { "0:tx1", "3:tx2" }, Placed(rig.MultiView.GetLive(rig.Rx))); // the screen state follows
            Assert.Equal("TX2", rig.RxVm.Slots.Single(s => s.Index == 3).SourceName); // and so does the card
            Assert.Equal(BannerKind.Warning, rig.Composer.Banner.Kind);
            Assert.Contains("changed elsewhere", rig.Composer.Banner.Text);
            Assert.Equal(new[] { "Block 2: empty -> TX2", "Block 4: TX2 -> empty" }, rig.Composer.ChangeLines);
            Assert.Empty(rig.Client.MultiViewCalls);

            // A kept (closed) draft also survives a restore.
            rig.Vm.CloseComposer();
            Assert.True(rig.RxVm.HasDraft);
            rig.Client.ReceiverStates["rx1"] = SingleState("rx1", "tx1", "TX1");
            await rig.Vm.RestoreStateAsync();

            Assert.True(rig.RxVm.HasDraft);
            Assert.False(rig.RxVm.IsMultiView);
            await rig.Vm.OpenComposerAsync(rig.RxVm);
            Assert.Equal(new[] { "0:tx1", "1:tx2" }, Placed(rig.Composer.Draft));
        }

        [Fact]
        public async Task Arm_AutoDisarmsAfter15sIdle()
        {
            var rig = new Rig();
            rig.Vm.ArmSource(rig.Tx1Vm);

            rig.Now += TimeSpan.FromSeconds(14.9);
            rig.Vm.Tick();
            Assert.Same(rig.Tx1Vm, rig.Vm.ArmedSource);

            rig.Vm.ArmSource(rig.Tx2Vm); // arming again resets the idle time
            rig.Now += TimeSpan.FromSeconds(10);
            rig.Vm.Tick();
            Assert.Same(rig.Tx2Vm, rig.Vm.ArmedSource);
            Assert.False(rig.Tx1Vm.IsArmed);

            rig.Now += TimeSpan.FromSeconds(5);
            rig.Vm.Tick();
            Assert.Null(rig.Vm.ArmedSource);
            Assert.False(rig.Tx2Vm.IsArmed);

            // In the Composer, placing counts as activity; idle still disarms there too.
            await rig.Vm.OpenComposerAsync(rig.RxVm, "matrix2x2");
            rig.Vm.ArmSource(rig.Tx1Vm);
            rig.Now += TimeSpan.FromSeconds(14);
            rig.Composer.TapBlock(0);
            rig.Now += TimeSpan.FromSeconds(14);
            rig.Vm.Tick();
            Assert.Same(rig.Tx1Vm, rig.Composer.ArmedSource);
            rig.Now += TimeSpan.FromSeconds(1);
            rig.Vm.Tick();
            Assert.Null(rig.Composer.ArmedSource);
            Assert.Null(rig.Vm.ArmedSource);
            Assert.False(rig.Tx1Vm.IsArmed);
        }

        // ---- carry-overs from the Task 4 review ----

        // The in-flight tests run on one thread with a posting context, as the app does on the WPF dispatcher: view
        // models are single-threaded, so continuations must not run them from the thread pool in parallel.

        [Fact]
        public void RouteIssuedWhileATakeIsInFlight_WaitsAndRunsAfterIt() => UiThread.Run(async () =>
        {
            var rig = new Rig();
            await rig.Vm.OpenComposerAsync(rig.RxVm, "matrix2x2");
            rig.Composer.Arm(rig.Tx1Vm);
            rig.Composer.TapBlock(0);
            var (entered, release) = HoldFirstCall(rig.Client, "multiview:");

            var take = rig.Composer.TakeAsync();
            await entered; // the Take is on the wire
            var route = rig.Vm.RouteVideoAsync(rig.Tx2Vm.Device, rig.Rx);
            await Task.Delay(100);
            Assert.False(route.IsCompleted);
            Assert.Null(rig.Client.LastVideoTx); // the join did not slip in under the Take

            release();
            await Within(Task.WhenAll(take, route), "Take + Route");

            Assert.True(await route);
            var calls = rig.Client.MultiViewCalls;
            Assert.True(calls.FindIndex(c => c.StartsWith("multiview:")) < calls.IndexOf("fastswitch:rx1:1920x1080@60"),
                string.Join(" | ", calls));
            Assert.Equal("tx2", rig.Client.LastVideoTx);
            Assert.False(rig.MultiView.IsMultiView(rig.Rx)); // the Route won, and everybody agrees
            Assert.False(rig.RxVm.IsMultiView);
            Assert.Equal("tx2", rig.RxVm.ActiveSourceId);
            Assert.Empty(rig.Composer.LiveBlocks); // the open Composer follows the screen
        });

        [Fact]
        public void OffIssuedWhileATakeIsInFlight_WaitsThenLeavesEverything() => UiThread.Run(async () =>
        {
            var rig = new Rig();
            await rig.TakeMatrixAsync();
            await rig.Vm.OpenComposerAsync(rig.RxVm);
            rig.Composer.Arm(rig.Tx2Vm);
            rig.Composer.TapBlock(1);
            var (entered, release) = HoldFirstCall(rig.Client, "multiview:");

            var take = rig.Composer.TakeAsync();
            await entered;
            var off = rig.Vm.UnrouteVideoAsync(rig.Rx);
            release();
            await Within(Task.WhenAll(take, off), "Take + Off");

            Assert.True(await off);
            Assert.False(rig.MultiView.IsMultiView(rig.Rx));
            Assert.False(rig.RxVm.IsMultiView);
            Assert.Contains("rx1:2", rig.Client.Leaves); // Off left the source the Take had just added
            Assert.Equal("fastswitch:rx1:1920x1080@60", rig.Client.MultiViewCalls.Last());
        });

        [Fact]
        public void RestoreIssuedWhileATakeIsInFlight_ReadsAfterTheTake() => UiThread.Run(async () =>
        {
            var rig = new Rig();
            await rig.Vm.OpenComposerAsync(rig.RxVm, "matrix2x2");
            rig.Composer.Arm(rig.Tx1Vm);
            rig.Composer.TapBlock(0);
            rig.Client.ReceiverStates["rx1"] = MatrixState("rx1", (0, 1, "tx1", "TX1"));
            var (entered, release) = HoldFirstCall(rig.Client, "multiview:");

            var take = rig.Composer.TakeAsync();
            await entered;
            var restore = rig.Vm.RestoreDisplayAsync(rig.RxVm);
            await Task.Delay(100);
            Assert.Empty(rig.Client.ReceiverReads); // waits: a read now would be the picture before the Take

            release();
            await Within(Task.WhenAll(take, restore), "Take + Restore");

            Assert.Single(rig.Client.ReceiverReads);
            Assert.True(rig.RxVm.IsMultiView);
            Assert.Equal(new[] { "0:tx1" }, Placed(rig.MultiView.GetLive(rig.Rx)));
        });

        [Fact]
        public async Task TakeOverAnExternalLayout_LeavesTheExternalSubscriptions()
        {
            var rig = new Rig();
            rig.Client.ReceiverStates["rx1"] = new RxState("rx1", "RX1", "MULTIVIEW", 1920, 1080, 60,
                new[] { new RxSubscription(3, true, "STREAMING", "tx2", "TX2", 1) },
                new RxLayout("bavm_custom", 1920, 1080, new[] { new RxWindow(0, 3) }));
            await rig.Vm.RestoreStateAsync();
            Assert.True(rig.RxVm.IsExternalLayout);

            await rig.Vm.ChooseLayoutFromCardAsync(rig.RxVm, "matrix2x2");
            rig.Composer.Arm(rig.Tx1Vm);
            rig.Composer.TapBlock(0);
            await rig.Composer.TakeAsync();

            Assert.Equal(BannerKind.Success, rig.Composer.Banner.Kind);
            Assert.Contains("rx1:3", rig.Client.Leaves);
            Assert.False(rig.RxVm.IsExternalLayout);
            Assert.True(rig.RxVm.IsMultiView);
            Assert.Equal("MATRIX 2X2 · 1/4", rig.RxVm.ModeBadge);

            // From now on the display is ours: Off turns the multiview off (no "external" path left behind).
            rig.Vm.CloseComposer();
            Assert.True(await rig.Vm.UnrouteVideoAsync(rig.Rx));
            Assert.Contains("rx1:1", rig.Client.Leaves);
        }

        // ---- carry-overs from the Task 5/6/9 reviews (Task 10-pre) ----

        [Fact]
        public void DeviceEvent_IsPostedToTheUiThread_BeforeAnythingRuns()
        {
            var posted = new List<Action>();
            var rig = new Rig(posted.Add);

            rig.Client.RaiseDeviceStateChanged("rx1", false); // raised on the client's read loop thread

            Assert.Single(posted);
            Assert.True(rig.RxVm.IsActive); // nothing ran on the caller's thread
            posted[0]();
            Assert.False(rig.RxVm.IsActive);
            Assert.Equal(1, rig.Vm.OnlineDisplaysCount);
        }

        [Fact]
        public void DeviceEvent_ComingBackOnline_ReadsTheReceiverOnlyOnceTheUiThreadRunsIt()
        {
            var posted = new List<Action>();
            var rig = new Rig(posted.Add);
            rig.RxVm.SetOnlineState(false);

            rig.Client.RaiseDeviceStateChanged("rx1", true);

            Assert.Empty(rig.Client.ReceiverReads);
            Assert.False(rig.RxVm.IsActive);
            posted.Single()();
            Assert.True(rig.RxVm.IsActive);
            Assert.Contains("rx1", rig.Client.ReceiverReads);
        }

        [Fact]
        public void DeviceEvent_WithAnInlinePoster_RunsInline()
        {
            // (The default poster is inline too when there is no WPF Application; other tests may create one, so the
            // default is not asserted here.)
            var rig = new Rig(action => action());

            rig.Client.RaiseDeviceStateChanged("rx1", false);

            Assert.False(rig.RxVm.IsActive);
        }

        [Fact]
        public async Task ArmBannerText_FollowsTheArmedSource_AndNeverShowsWithTheCopyBanner()
        {
            var rig = new Rig();
            var raised = new List<string?>();
            rig.Vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            Assert.Null(rig.Vm.ArmBannerText);
            Assert.Null(rig.Vm.CopyArmBanner);

            rig.Vm.ArmSource(rig.Tx2Vm);
            Assert.Equal("Placing TX2 - tap a display or a block. Tap TX2 again to stop.", rig.Vm.ArmBannerText);
            Assert.Null(rig.Vm.CopyArmBanner);
            Assert.Contains(nameof(MainViewModel.ArmBannerText), raised);

            raised.Clear();
            rig.Vm.ArmSource(rig.Tx2Vm); // tap again: stop
            Assert.Null(rig.Vm.ArmBannerText);
            Assert.Contains(nameof(MainViewModel.ArmBannerText), raised);

            // The Composer's own arm is mirrored.
            await rig.Vm.OpenComposerAsync(rig.RxVm, "matrix2x2");
            raised.Clear();
            rig.Composer.Arm(rig.Tx1Vm);
            Assert.Equal("Placing TX1 - tap a display or a block. Tap TX1 again to stop.", rig.Vm.ArmBannerText);
            Assert.Contains(nameof(MainViewModel.ArmBannerText), raised);
            raised.Clear();
            rig.Composer.Disarm();
            Assert.Null(rig.Vm.ArmBannerText);
            Assert.Contains(nameof(MainViewModel.ArmBannerText), raised);
            rig.Vm.CloseComposer();

            // A display armed for copying shows the copy banner instead.
            await rig.Vm.RouteVideoAsync(rig.Tx1Vm.Device, rig.Rx);
            rig.Vm.ArmSource(rig.Tx2Vm);
            await rig.Vm.ArmDisplayAsync(rig.RxVm);
            Assert.Null(rig.Vm.ArmBannerText);
            Assert.StartsWith("Copying RX1", rig.Vm.CopyArmBanner);
            rig.Vm.ArmSource(rig.Tx2Vm);
            Assert.NotNull(rig.Vm.ArmBannerText);
            Assert.Null(rig.Vm.CopyArmBanner);
        }

        [Fact]
        public void TakeInFlightWhenTheCardIsRebuilt_UpdatesTheNewCard_AndDropsItsStaleDraftChip() => UiThread.Run(async () =>
        {
            var rig = new Rig();
            await rig.Vm.OpenComposerAsync(rig.RxVm, "matrix2x2");
            rig.Composer.Arm(rig.Tx1Vm);
            rig.Composer.TapBlock(0);
            var (entered, release) = HoldFirstCall(rig.Client, "multiview:");
            var take = rig.Composer.TakeAsync();
            await entered;
            rig.Vm.CloseComposer(); // the Take is still on the wire: the draft is kept
            Assert.True(rig.RxVm.HasDraft);

            // A device refresh rebuilds the card (the draft chip comes back from the kept draft).
            rig.Vm.Displays.Remove(rig.RxVm);
            var fresh = new DisplayCardViewModel(rig.Rx, rig.Vm.UnrouteVideoAsync) { HasDraft = true };
            rig.Vm.Displays.Add(fresh);

            release();
            await Within(take, "Take");

            Assert.True(fresh.IsMultiView); // resolved by MAC, not by the old card
            Assert.False(fresh.HasDraft); // what the draft held is on screen now
        });

        [Fact]
        public async Task RestoreThatShowsTheKeptDraft_DropsTheDraftChip()
        {
            var rig = new Rig();
            await rig.Vm.OpenComposerAsync(rig.RxVm, "matrix2x2");
            rig.Composer.Arm(rig.Tx1Vm);
            rig.Composer.TapBlock(0);
            rig.Vm.CloseComposer();
            Assert.True(rig.RxVm.HasDraft);

            rig.Client.ReceiverStates["rx1"] = MatrixState("rx1", (0, 1, "tx1", "TX1")); // someone put the same picture on screen
            await rig.Vm.RestoreDisplayAsync(rig.RxVm);

            Assert.True(rig.RxVm.IsMultiView);
            Assert.False(rig.RxVm.HasDraft);
        }

        [Fact]
        public async Task RouteAndOff_ReCheckTheKeptDraft_SoAStaleChipGoes()
        {
            var rig = new Rig();
            rig.RxVm.HasDraft = true; // a chip with no kept draft behind it (a rebuilt card)
            rig.RxVm.DraftNotice = "old";

            await rig.Vm.RouteVideoAsync(rig.Tx1Vm.Device, rig.Rx);

            Assert.False(rig.RxVm.HasDraft);
            Assert.Null(rig.RxVm.DraftNotice);

            rig.RxVm.HasDraft = true;
            await rig.Vm.UnrouteVideoAsync(rig.Rx);

            Assert.False(rig.RxVm.HasDraft);
        }

        [Fact]
        public void SecondComposer_IsToldAboutAClosedComposersTake() => UiThread.Run(async () =>
        {
            var rig = new Rig();
            await rig.Vm.OpenComposerAsync(rig.RxVm, "matrix2x2");
            rig.Composer.Arm(rig.Tx1Vm);
            rig.Composer.TapBlock(0);
            var (entered, release) = HoldFirstCall(rig.Client, "multiview:");
            var take = rig.Composer.TakeAsync();
            await entered;
            rig.Vm.CloseComposer(); // keeps the draft; the Take is still in flight
            await rig.Vm.OpenComposerAsync(rig.RxVm); // the second Composer resumes it
            var second = rig.Composer;
            Assert.Empty(second.LiveBlocks);
            Assert.Single(second.Changes);

            release();
            await Within(take, "Take");

            Assert.Single(second.LiveBlocks, b => b.IsPlaced); // ON SCREEN NOW follows
            Assert.Empty(second.Changes); // and the change list is refreshed
        });

        [Fact]
        public async Task OpeningAComposer_ClosesEveryOpenDropChoice()
        {
            var rig = new Rig();
            rig.Vm.OpenDropChoice(rig.Rx2Vm, rig.Tx2Vm);
            Assert.True(rig.Rx2Vm.IsDropChoiceOpen);

            await rig.Vm.OpenComposerAsync(rig.RxVm, "matrix2x2");

            Assert.False(rig.Rx2Vm.IsDropChoiceOpen);
            Assert.Null(rig.Rx2Vm.DropChoiceSource);
        }

        [Fact]
        public void UiThreadHelper_ThrowsAtItsDeadline_InsteadOfHanging()
        {
            var started = DateTime.UtcNow;

            Assert.Throws<TimeoutException>(() =>
                UiThread.Run(() => new TaskCompletionSource().Task, TimeSpan.FromMilliseconds(200)));

            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        }

        // ---- more of the overview ----

        [Fact]
        public async Task ArmSource_TogglesAndOnlyOneSourceIsArmed()
        {
            var rig = new Rig();

            rig.Vm.ArmSource(rig.Tx1Vm);
            rig.Vm.ArmSource(rig.Tx2Vm);
            Assert.False(rig.Tx1Vm.IsArmed);
            Assert.True(rig.Tx2Vm.IsArmed);

            rig.Vm.ArmSource(rig.Tx2Vm);
            Assert.Null(rig.Vm.ArmedSource);
            Assert.False(rig.Tx2Vm.IsArmed);

            await rig.Vm.TapDisplayAsync(rig.RxVm); // nothing armed: no-op
            Assert.Null(rig.Client.LastVideoTx);
            Assert.Null(rig.Vm.ActiveComposer);
        }

        [Fact]
        public async Task DropChoice_ClosesAfter10s()
        {
            var rig = new Rig();
            await rig.TakeMatrixAsync();
            rig.Vm.OpenDropChoice(rig.RxVm, rig.Tx2Vm);
            Assert.True(rig.RxVm.IsDropChoiceOpen);

            rig.Now += TimeSpan.FromSeconds(9.9);
            rig.Vm.Tick();
            Assert.True(rig.RxVm.IsDropChoiceOpen);

            rig.Now += TimeSpan.FromSeconds(0.1);
            rig.Vm.Tick();
            Assert.False(rig.RxVm.IsDropChoiceOpen);
            Assert.Null(rig.RxVm.DropChoiceSource);
        }

        [Fact]
        public async Task DroppingASource_OnAMultiViewCard_OpensTheChoice_OnASingleViewCard_Routes()
        {
            var rig = new Rig();
            await rig.TakeMatrixAsync();

            Assert.True(await rig.Vm.DropSourceAsync(rig.RxVm, rig.Tx2Vm.Device));
            Assert.True(rig.RxVm.IsDropChoiceOpen);
            Assert.Same(rig.Tx2Vm, rig.RxVm.DropChoiceSource);
            Assert.Null(rig.Client.LastVideoTx);

            Assert.True(await rig.Vm.DropSourceAsync(rig.Rx2Vm, rig.Tx2Vm.Device));
            Assert.Equal("rx2", rig.Client.LastVideoRx);
            Assert.Equal("tx2", rig.Rx2Vm.ActiveSourceId);

            var notTx = new AvasDevice { DeviceId = "rx9", DeviceName = "RX9", IsReceiver = true, IsActive = true };
            Assert.False(await rig.Vm.DropSourceAsync(rig.Rx2Vm, notTx));
            Assert.Equal("tx2", rig.Rx2Vm.ActiveSourceId);
        }

        [Fact]
        public async Task OpeningAComposerForAnotherDisplay_ClosesTheFirst_KeepingItsDraft()
        {
            var rig = new Rig();
            await rig.Vm.OpenComposerAsync(rig.RxVm, "matrix2x2");
            var first = rig.Composer;
            first.Arm(rig.Tx1Vm);
            first.TapBlock(0);

            await rig.Vm.OpenComposerAsync(rig.Rx2Vm, "pip");

            Assert.NotSame(first, rig.Vm.ActiveComposer);
            Assert.Equal("pip", rig.Composer.SelectedLayoutId);
            Assert.True(rig.RxVm.HasDraft);
            Assert.False(rig.Rx2Vm.HasDraft);
            Assert.Null(rig.Vm.ArmedSource); // the first composer's arm went with it
            Assert.False(rig.Tx1Vm.IsArmed);

            await rig.Vm.OpenComposerAsync(rig.RxVm); // back to the first display: its draft resumes
            Assert.Equal(new[] { "0:tx1" }, Placed(rig.Composer.Draft));
            Assert.False(rig.Rx2Vm.HasDraft); // RX2 had nothing to keep (a layout, nothing placed)
        }

        [Fact]
        public async Task CloseComposer_WithoutChanges_KeepsNoDraft()
        {
            var rig = new Rig();
            await rig.TakeMatrixAsync();
            Assert.False(rig.RxVm.HasDraft); // the Take sent everything

            await rig.Vm.OpenComposerAsync(rig.RxVm);
            rig.Vm.CloseComposer();
            Assert.False(rig.RxVm.HasDraft);

            await rig.Vm.OpenComposerAsync(rig.Rx2Vm, "matrix2x2"); // a layout but nothing placed, nothing on screen
            rig.Vm.CloseComposer();
            Assert.False(rig.Rx2Vm.HasDraft);
        }

        [Fact]
        public async Task OpenComposer_WithAnotherPresetOnAMultiViewDisplay_StagesItEmpty_NoDeviceChange()
        {
            var rig = new Rig();
            await rig.TakeMatrixAsync();

            await rig.Vm.ChooseLayoutFromCardAsync(rig.RxVm, "side_by_side");

            Assert.Equal("side_by_side", rig.Composer.SelectedLayoutId);
            Assert.Empty(rig.Composer.Draft!.Placements); // a new layout opens with empty blocks (spec 5.1)
            Assert.Single(rig.Composer.LiveBlocks.Where(b => b.IsPlaced)); // ON SCREEN NOW still shows the matrix
            Assert.Equal("matrix2x2", rig.RxVm.LayoutId); // the card shows the screen
            Assert.Empty(rig.Client.MultiViewCalls);
        }

        [Fact]
        public async Task OpenComposer_WithAnotherPresetOverAKeptDraft_KeepsItsSourcesByBlockOrder()
        {
            var rig = new Rig();
            await rig.Vm.OpenComposerAsync(rig.RxVm, "matrix2x2");
            rig.Composer.Arm(rig.Tx1Vm);
            rig.Composer.TapBlock(0);
            rig.Vm.CloseComposer();

            await rig.Vm.ChooseLayoutFromCardAsync(rig.RxVm, "side_by_side");

            Assert.Equal("side_by_side", rig.Composer.SelectedLayoutId);
            Assert.Equal(new[] { "tx1" }, rig.Composer.Draft!.Placements.Values.Select(p => p.SourceMac));
            Assert.Empty(rig.Client.MultiViewCalls);
        }

        [Fact]
        public async Task OpenComposer_OnAnOfflineDisplay_IsRefused()
        {
            var rig = new Rig();
            rig.RxVm.SetOnlineState(false);

            await rig.Vm.OpenComposerAsync(rig.RxVm, "matrix2x2");
            bool ok = await rig.Vm.ChooseLayoutFromCardAsync(rig.RxVm, "matrix2x2");

            Assert.False(ok);
            Assert.Null(rig.Vm.ActiveComposer);
            Assert.False(rig.RxVm.IsMultiView);
        }

        [Fact]
        public async Task ChooseLayoutFromCard_UnknownId_IsRefused()
        {
            var rig = new Rig();

            Assert.False(await rig.Vm.ChooseLayoutFromCardAsync(rig.RxVm, "nope"));
            Assert.Null(rig.Vm.ActiveComposer);
            Assert.False(rig.RxVm.IsMultiView);
        }

        [Fact]
        public async Task RestoredMultiView_WithAMissingEncoderCard_StaysOnTheCard_AndIsAnIssueInTheComposer()
        {
            var rig = new Rig();
            rig.Client.ReceiverStates["rx1"] = MatrixState("rx1", (0, 1, "tx1", "TX1"), (1, 2, "tx9", "TX9"));
            await rig.Vm.RestoreStateAsync();

            Assert.Equal("TX9", rig.RxVm.Slots.Single(s => s.Index == 1).SourceName);
            await rig.Vm.OpenComposerAsync(rig.RxVm);

            Assert.Equal(BlockState.Issue, rig.Composer.Blocks.Single(b => b.Index == 1).State);
            Assert.False(rig.Composer.CanTake);
            Assert.Equal(0, rig.Tx2Vm.UsedInBlocks);
        }
    }

    /// <summary>
    /// Runs an async test body on one thread with a posting <see cref="SynchronizationContext"/>, like the WPF
    /// dispatcher: every continuation of the view models comes back to that thread, one at a time.
    /// </summary>
    internal static class UiThread
    {
        /// <summary>How long a test body may take before the pump gives up (a deadlocked test fails instead of hanging the run).</summary>
        public static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(60);

        /// <param name="deadline">Overall limit for the body; a <see cref="TimeoutException"/> is thrown when it is exceeded.</param>
        public static void Run(Func<Task> body, TimeSpan? deadline = null)
        {
            var previous = SynchronizationContext.Current;
            var context = new PumpContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var task = body();
                task.ContinueWith(_ => context.Complete(), TaskScheduler.Default);
                context.RunUntilComplete(deadline ?? DefaultDeadline);
                task.GetAwaiter().GetResult();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        private sealed class PumpContext : SynchronizationContext
        {
            private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

            public override void Post(SendOrPostCallback d, object? state)
            {
                try
                {
                    _queue.Add((d, state));
                }
                catch (InvalidOperationException)
                {
                    ThreadPool.QueueUserWorkItem(_ => d(state)); // posted after the body ended
                }
            }

            public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

            public void RunUntilComplete(TimeSpan deadline)
            {
                var stop = DateTime.UtcNow + deadline;
                while (true)
                {
                    var left = stop - DateTime.UtcNow;
                    if (left > TimeSpan.Zero && _queue.TryTake(out var item, left))
                    {
                        item.Callback(item.State);
                        continue;
                    }
                    if (_queue.IsCompleted) return; // the body finished and everything it posted has run
                    throw new TimeoutException($"The test body did not finish within {deadline.TotalSeconds:0.##} s: deadlock?");
                }
            }

            public void Complete() => _queue.CompleteAdding();
        }
    }
}
