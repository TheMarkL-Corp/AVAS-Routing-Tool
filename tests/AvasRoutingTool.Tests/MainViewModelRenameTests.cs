using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// A client that can rename (like the real SdvoeClient) with a scripted outcome. It plays the Control Server: a successful
    /// rename changes the name its next discovery returns, a failed one changes nothing. Discovery and the rename call can each be held.
    /// </summary>
    public class RenamingMockClient : MockSdvoeClient, ISdvoeClient, IDeviceRenamer
    {
        /// <summary>The Control Server's devices; every discovery returns fresh copies of these.</summary>
        public List<AvasDevice> Server { get; } = new();

        public RenameResult Result { get; set; } = RenameResult.Success;

        /// <summary>Every rename call, as (mac, newName).</summary>
        public List<(string Mac, string Name)> RenameCalls { get; } = new();

        public int DiscoverCount { get; private set; }

        /// <summary>When set, the rename call waits for it (the write is in flight).</summary>
        public TaskCompletionSource<bool>? RenameGate { get; set; }

        /// <summary>When set, discovery waits for it.</summary>
        public TaskCompletionSource<bool>? DiscoverGate { get; set; }

        public async Task<RenameResult> RenameDeviceAsync(string mac, string newName)
        {
            RenameCalls.Add((mac, newName));
            if (RenameGate != null) await RenameGate.Task;
            if (!Result.Ok) return Result;

            string norm = SdvoeClient.NormalizeMac(mac);
            foreach (var d in Server.Where(d => SdvoeClient.NormalizeMac(d.DeviceId) == norm)) d.DeviceName = newName;
            return Result;
        }

        public new async Task<List<AvasDevice>> DiscoverDevicesAsync()
        {
            DiscoverCount++;
            if (DiscoverGate != null) await DiscoverGate.Task;
            return Server.Select(d => new AvasDevice
            {
                DeviceId = d.DeviceId, DeviceName = d.DeviceName, VendorId = d.VendorId, IsTransmitter = d.IsTransmitter,
                IsReceiver = d.IsReceiver, IsActive = d.IsActive, ActiveSourceId = d.ActiveSourceId, ActiveSourceName = d.ActiveSourceName
            }).ToList();
        }
    }

    /// <summary>MainViewModel.RenameDeviceAsync (plan R4 part A): ask for a name, write it to the server, then show it only after rediscovery.</summary>
    public class MainViewModelRenameTests
    {
        private const string Tx1Mac = "74fe48000001";
        private const string Tx2Mac = "74fe48000002";
        private const string Rx1Mac = "74fe48000011";
        private const string Rx2Mac = "74fe48000012";

        private sealed class Rig
        {
            public RenamingMockClient Client { get; } = new();
            public MainViewModel Vm { get; }
            public List<RenameDialogViewModel> Asked { get; } = new();

            /// <summary>What the dialog returns (null = cancelled).</summary>
            public string? Answer { get; set; }

            public Rig()
            {
                // Non-AVAS sources (vendor 192) so discovery starts no preview stream.
                Client.Server.Add(new AvasDevice { DeviceId = Tx1Mac, DeviceName = "Alpha TX", VendorId = 192, IsTransmitter = true, IsActive = true });
                Client.Server.Add(new AvasDevice { DeviceId = Tx2Mac, DeviceName = "Lobby TX", VendorId = 192, IsTransmitter = true, IsActive = true });
                Client.Server.Add(new AvasDevice { DeviceId = Rx1Mac, DeviceName = "Wall RX", IsReceiver = true, IsActive = true, ActiveSourceId = Tx1Mac, ActiveSourceName = "Alpha TX" });
                Client.Server.Add(new AvasDevice { DeviceId = Rx2Mac, DeviceName = "Bar RX", IsReceiver = true, IsActive = true });

                Vm = new MainViewModel(new ConfigService(), Client, new MockRtpReceiver(), new MulticastIpManager(),
                    new RoutingOrchestrator(Client), new MultiViewOrchestrator(Client), post: a => a());
                Vm.AskForName = dialog =>
                {
                    Asked.Add(dialog);
                    return Answer;
                };
            }

            public async Task<Rig> DiscoveredAsync()
            {
                await Vm.RefreshDevicesAsync();
                return this;
            }

            public SourceCardViewModel Source(string mac) => Vm.Sources.Single(s => s.MacAddress == mac);
            public DisplayCardViewModel Display(string mac) => Vm.Displays.Single(d => d.MacAddress == mac);
            public string[] SourceNames => Vm.Sources.Select(s => s.DeviceName).ToArray();
            public string[] DisplayNames => Vm.Displays.Select(d => d.DeviceName).ToArray();
        }

        private static async Task WaitUntil(Func<bool> condition, string what)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.True(condition(), what);
        }

        // ---- success -------------------------------------------------------------------

        [Fact]
        public async Task Success_WritesTheNameOnce_RebuildsTheCards_AndClearsTheBanner()
        {
            var rig = await new Rig { Answer = "Stage left" }.DiscoveredAsync();
            var device = rig.Source(Tx2Mac).Device;

            await rig.Vm.RenameDeviceAsync(device);

            Assert.Equal(new[] { (Tx2Mac, "Stage left") }, rig.Client.RenameCalls);
            Assert.Equal(2, rig.Client.DiscoverCount);
            Assert.Contains("Stage left", rig.SourceNames);
            Assert.DoesNotContain("Lobby TX", rig.SourceNames);
            Assert.Null(rig.Vm.RenameBanner);
            Assert.False(rig.Vm.HasRenameBanner);
            Assert.False(rig.Vm.IsRenaming);
        }

        [Fact]
        public async Task Success_ShowsTheNewName_OnlyAfterTheServerConfirmedAndDiscoveryReturnedIt()
        {
            var rig = await new Rig { Answer = "Stage left" }.DiscoveredAsync();
            var device = rig.Source(Tx2Mac).Device;
            rig.Client.RenameGate = new TaskCompletionSource<bool>();

            var rename = rig.Vm.RenameDeviceAsync(device);
            await WaitUntil(() => rig.Client.RenameCalls.Count == 1, "the rename call was made");

            // The write is in flight: nothing changed on the cards, and the view model says it is busy.
            Assert.True(rig.Vm.IsRenaming);
            Assert.Contains("Lobby TX", rig.SourceNames);
            Assert.DoesNotContain("Stage left", rig.SourceNames);

            // The server answered; discovery has not returned yet: still the old name.
            rig.Client.DiscoverGate = new TaskCompletionSource<bool>();
            rig.Client.RenameGate.SetResult(true);
            await WaitUntil(() => rig.Client.DiscoverCount == 2, "rediscovery started after the rename succeeded");
            Assert.Contains("Lobby TX", rig.SourceNames);
            Assert.DoesNotContain("Stage left", rig.SourceNames);

            rig.Client.DiscoverGate.SetResult(true);
            await rename;

            Assert.Contains("Stage left", rig.SourceNames);
            Assert.DoesNotContain("Lobby TX", rig.SourceNames);
            Assert.False(rig.Vm.IsRenaming);
        }

        [Fact]
        public async Task Success_WorksForADisplay_Too()
        {
            var rig = await new Rig { Answer = "Bar screen" }.DiscoveredAsync();

            await rig.Vm.RenameDeviceAsync(rig.Display(Rx2Mac).Device);

            Assert.Equal(new[] { (Rx2Mac, "Bar screen") }, rig.Client.RenameCalls);
            Assert.Contains("Bar screen", rig.DisplayNames);
            Assert.DoesNotContain("Bar RX", rig.DisplayNames);
        }

        // ---- failure -------------------------------------------------------------------

        [Fact]
        public async Task ServerRejects_ShowsTheBanner_KeepsTheOldName_AndDoesNotRediscover()
        {
            var rig = await new Rig { Answer = "Stage left" }.DiscoveredAsync();
            var card = rig.Source(Tx2Mac);
            rig.Client.Result = RenameResult.Failed("Device name too long, must be 19 characters at most");

            await rig.Vm.RenameDeviceAsync(card.Device);

            Assert.Equal("Couldn't rename Lobby TX: Device name too long, must be 19 characters at most", rig.Vm.RenameBanner);
            Assert.True(rig.Vm.HasRenameBanner);
            Assert.Equal("Lobby TX", card.DeviceName);
            Assert.Contains("Lobby TX", rig.SourceNames);
            Assert.Same(card, rig.Source(Tx2Mac)); // the cards were not rebuilt
            Assert.Equal(1, rig.Client.DiscoverCount); // only the first discovery
            Assert.False(rig.Vm.IsRenaming);
        }

        [Fact]
        public async Task TransportFailure_ShowsTheBanner_AndKeepsTheOldName()
        {
            var rig = await new Rig { Answer = "Stage left" }.DiscoveredAsync();
            rig.Client.Result = RenameResult.Failed("Couldn't reach the Control Server: connection refused");

            await rig.Vm.RenameDeviceAsync(rig.Display(Rx2Mac).Device);

            Assert.Equal("Couldn't rename Bar RX: Couldn't reach the Control Server: connection refused", rig.Vm.RenameBanner);
            Assert.Contains("Bar RX", rig.DisplayNames);
            Assert.Equal(1, rig.Client.DiscoverCount);
        }

        [Fact]
        public async Task AClientThatCannotRename_ShowsAPlainBanner_AndAsksNothing()
        {
            // A plain mock is not an IDeviceRenamer.
            var client = new MockSdvoeClient();
            var vm = new MainViewModel(new ConfigService(), client, new MockRtpReceiver(), new MulticastIpManager(),
                new RoutingOrchestrator(client), new MultiViewOrchestrator(client), post: a => a());
            int asked = 0;
            vm.AskForName = _ => { asked++; return "Whatever"; };

            await vm.RenameDeviceAsync(new AvasDevice { DeviceId = Tx1Mac, DeviceName = "Alpha TX", IsTransmitter = true });

            Assert.Equal("Couldn't rename Alpha TX: this connection can't rename devices", vm.RenameBanner);
            Assert.Equal(0, asked);
        }

        // ---- no call -------------------------------------------------------------------

        [Fact]
        public async Task Cancel_MakesNoCall_AndShowsNoBanner()
        {
            var rig = await new Rig { Answer = null }.DiscoveredAsync();

            await rig.Vm.RenameDeviceAsync(rig.Source(Tx2Mac).Device);

            Assert.Single(rig.Asked);
            Assert.Empty(rig.Client.RenameCalls);
            Assert.Null(rig.Vm.RenameBanner);
            Assert.Equal(1, rig.Client.DiscoverCount);
        }

        [Fact]
        public async Task TheSameNameBack_MakesNoCall()
        {
            var rig = await new Rig { Answer = "Lobby TX" }.DiscoveredAsync();

            await rig.Vm.RenameDeviceAsync(rig.Source(Tx2Mac).Device);

            Assert.Empty(rig.Client.RenameCalls);
            Assert.Null(rig.Vm.RenameBanner);
            Assert.Equal(1, rig.Client.DiscoverCount);
        }

        [Fact]
        public async Task WithoutADialogSeam_NothingHappens()
        {
            var rig = await new Rig().DiscoveredAsync();
            rig.Vm.AskForName = null;

            await rig.Vm.RenameDeviceAsync(rig.Source(Tx2Mac).Device);

            Assert.Empty(rig.Client.RenameCalls);
            Assert.Null(rig.Vm.RenameBanner);
        }

        // ---- what the dialog is told ---------------------------------------------------

        [Fact]
        public async Task TheDialog_GetsTheDevicesName_AndEveryOtherDeviceAsTaken()
        {
            var rig = await new Rig { Answer = null }.DiscoveredAsync();

            await rig.Vm.RenameDeviceAsync(rig.Source(Tx2Mac).Device);

            var dialog = Assert.Single(rig.Asked);
            Assert.Equal("Rename Lobby TX", dialog.Title);
            Assert.Equal("Lobby TX", dialog.CurrentName);

            // The other three devices (a source and both displays) are taken, whatever the case.
            foreach (var other in new[] { "alpha tx", "WALL RX", "bar rx" })
            {
                dialog.Name = other;
                Assert.False(dialog.CanRename, other);
                Assert.Equal("Another device already has this name", dialog.Reason);
            }

            // The device's own name is not "taken": it is just unchanged.
            dialog.Name = "LOBBY TX";
            Assert.True(dialog.CanRename);
            dialog.Name = "Lobby TX";
            Assert.Equal("Type a different name", dialog.Reason);
        }

        [Fact]
        public async Task TheDeviceItself_IsLeftOut_WhateverTheFormatOfItsMac()
        {
            var rig = await new Rig { Answer = null }.DiscoveredAsync();
            var sameDeviceOtherFormat = new AvasDevice { DeviceId = "74:FE:48:00:00:02", DeviceName = "Lobby TX", IsTransmitter = true };

            await rig.Vm.RenameDeviceAsync(sameDeviceOtherFormat);

            var dialog = Assert.Single(rig.Asked);
            dialog.Name = "Lobby tx";
            Assert.NotEqual("Another device already has this name", dialog.Reason);
            dialog.Name = "Alpha TX";
            Assert.Equal("Another device already has this name", dialog.Reason);
        }

        // ---- routes --------------------------------------------------------------------

        [Fact]
        public async Task ARoutedSource_KeepsItsRoute_AfterTheRename()
        {
            var rig = await new Rig { Answer = "Stage left" }.DiscoveredAsync();

            await rig.Vm.RenameDeviceAsync(rig.Source(Tx1Mac).Device);

            var rx = rig.Display(Rx1Mac);
            Assert.Equal(Tx1Mac, rx.Device.ActiveSourceId);
            Assert.True(rx.IsRouted);

            // The display's picture still comes from that source's (new) card.
            var newSource = rig.Source(Tx1Mac);
            Assert.Equal("Stage left", newSource.DeviceName);
            var picture = new WriteableBitmap(8, 8, 96, 96, PixelFormats.Bgr24, null);
            newSource.PreviewBitmap = picture;
            Assert.Same(picture, rx.PreviewBitmap);
        }

        [Fact]
        public async Task ARoutedDisplay_KeepsItsRoute_AfterTheRename()
        {
            var rig = await new Rig { Answer = "Wall screen" }.DiscoveredAsync();

            await rig.Vm.RenameDeviceAsync(rig.Display(Rx1Mac).Device);

            var rx = rig.Display(Rx1Mac);
            Assert.Equal("Wall screen", rx.DeviceName);
            Assert.Equal(Tx1Mac, rx.Device.ActiveSourceId);
            Assert.True(rx.IsRouted);
            var picture = new WriteableBitmap(8, 8, 96, 96, PixelFormats.Bgr24, null);
            rig.Source(Tx1Mac).PreviewBitmap = picture;
            Assert.Same(picture, rx.PreviewBitmap);
        }

        // ---- busy ----------------------------------------------------------------------

        [Fact]
        public async Task ASecondRename_WhileOneIsInFlight_DoesNothing()
        {
            var rig = await new Rig { Answer = "Stage left" }.DiscoveredAsync();
            rig.Client.RenameGate = new TaskCompletionSource<bool>();

            var first = rig.Vm.RenameDeviceAsync(rig.Source(Tx2Mac).Device);
            await WaitUntil(() => rig.Client.RenameCalls.Count == 1, "the first rename call was made");
            Assert.True(rig.Vm.IsRenaming);

            await rig.Vm.RenameDeviceAsync(rig.Display(Rx2Mac).Device);

            Assert.Single(rig.Asked);
            Assert.Single(rig.Client.RenameCalls);

            rig.Client.RenameGate.SetResult(true);
            await first;
            Assert.False(rig.Vm.IsRenaming);
        }

        [Fact]
        public async Task IsRenaming_IsClearedAgain_EvenWhenTheCallThrows()
        {
            var rig = await new Rig { Answer = "Stage left" }.DiscoveredAsync();
            rig.Client.RenameGate = new TaskCompletionSource<bool>();
            var first = rig.Vm.RenameDeviceAsync(rig.Source(Tx2Mac).Device);
            await WaitUntil(() => rig.Client.RenameCalls.Count == 1, "the rename call was made");

            rig.Client.RenameGate.SetException(new InvalidOperationException("boom"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => first);

            Assert.False(rig.Vm.IsRenaming);
        }

        // ---- the banner ----------------------------------------------------------------

        [Fact]
        public async Task DismissRenameBannerCommand_ClearsTheBanner()
        {
            var rig = await new Rig { Answer = "Stage left" }.DiscoveredAsync();
            rig.Client.Result = RenameResult.Failed("Nope");
            await rig.Vm.RenameDeviceAsync(rig.Source(Tx2Mac).Device);
            Assert.NotNull(rig.Vm.RenameBanner);

            Assert.True(rig.Vm.DismissRenameBannerCommand.CanExecute(null));
            rig.Vm.DismissRenameBannerCommand.Execute(null);

            Assert.Null(rig.Vm.RenameBanner);
            Assert.False(rig.Vm.HasRenameBanner);
        }

        [Fact]
        public async Task ANewAttempt_ClearsTheOldBanner()
        {
            var rig = await new Rig { Answer = "Stage left" }.DiscoveredAsync();
            rig.Client.Result = RenameResult.Failed("Nope");
            await rig.Vm.RenameDeviceAsync(rig.Source(Tx2Mac).Device);
            Assert.NotNull(rig.Vm.RenameBanner);

            // The next attempt is cancelled in the dialog: the old failure is gone anyway.
            rig.Answer = null;
            await rig.Vm.RenameDeviceAsync(rig.Source(Tx2Mac).Device);

            Assert.Null(rig.Vm.RenameBanner);
        }

        [Fact]
        public async Task ABannerAndIsRenaming_RaisePropertyChanged()
        {
            var rig = await new Rig { Answer = "Stage left" }.DiscoveredAsync();
            rig.Client.Result = RenameResult.Failed("Nope");
            var raised = new List<string?>();
            ((INotifyPropertyChanged)rig.Vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            await rig.Vm.RenameDeviceAsync(rig.Source(Tx2Mac).Device);

            Assert.Contains(nameof(MainViewModel.RenameBanner), raised);
            Assert.Contains(nameof(MainViewModel.HasRenameBanner), raised);
            Assert.Contains(nameof(MainViewModel.IsRenaming), raised);
        }

        // ---- the card commands ---------------------------------------------------------

        [Fact]
        public async Task EveryCardRenameCommand_RunsTheRenameOfItsOwnDevice()
        {
            var rig = await new Rig { Answer = null }.DiscoveredAsync();

            rig.Source(Tx1Mac).RenameCommand.Execute(null);
            await WaitUntil(() => rig.Asked.Count == 1, "the source's pencil asked for a name");
            Assert.Equal("Alpha TX", rig.Asked[0].CurrentName);

            rig.Display(Rx2Mac).RenameCommand.Execute(null);
            await WaitUntil(() => rig.Asked.Count == 2, "the display's pencil asked for a name");
            Assert.Equal("Bar RX", rig.Asked[1].CurrentName);
        }

        [Fact]
        public void ACardWithoutAHandler_CannotRename_AndSpeaksItsName()
        {
            var source = new SourceCardViewModel(new AvasDevice { DeviceId = Tx1Mac, DeviceName = "Alpha TX", IsTransmitter = true });
            var display = new DisplayCardViewModel(new AvasDevice { DeviceId = Rx1Mac, DeviceName = "Wall RX", IsReceiver = true });

            Assert.False(source.RenameCommand.CanExecute(null));
            Assert.False(display.RenameCommand.CanExecute(null));
            Assert.Equal("Rename Alpha TX", source.RenameAutomationName);
            Assert.Equal("Rename Wall RX", display.RenameAutomationName);
            Assert.Equal("Rename", source.RenameToolTip);
            Assert.Equal("Rename", display.RenameToolTip);
        }

        [Fact]
        public async Task ACardWithAHandler_CanRename_AndRunsIt()
        {
            int ran = 0;
            var source = new SourceCardViewModel(new AvasDevice { DeviceId = Tx1Mac, DeviceName = "Alpha TX", IsTransmitter = true })
            {
                RenameHandler = () => { ran++; return Task.CompletedTask; }
            };
            var display = new DisplayCardViewModel(new AvasDevice { DeviceId = Rx1Mac, DeviceName = "Wall RX", IsReceiver = true })
            {
                RenameHandler = () => { ran++; return Task.CompletedTask; }
            };

            Assert.True(source.RenameCommand.CanExecute(null));
            Assert.True(display.RenameCommand.CanExecute(null));
            source.RenameCommand.Execute(null);
            display.RenameCommand.Execute(null);
            await WaitUntil(() => ran == 2, "both handlers ran");
        }
    }
}
