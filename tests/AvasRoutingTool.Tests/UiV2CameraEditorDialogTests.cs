using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Services;
using AvasRoutingTool.Services.Visca;
using AvasRoutingTool.Tests.Fakes;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>The camera editor dialog (RC12): Detect, Test connection, Save and its reason, Remove, Escape, close-cancels, both themes and sizes.</summary>
    [Collection("WpfSta")]
    public class UiV2CameraEditorDialogTests
    {
        private const string Host = "192.168.1.163";

        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    InputModality.IsKeyboard = false;
                    TestResources.Ensure();
                    body();
                }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("Camera editor failure: " + failure);
        }

        private static CameraEditorViewModel.DetectFunc Hits(params ProbeHit[] hits) =>
            (_, _, _, _) => Task.FromResult<IReadOnlyList<ProbeHit>>(hits);

        private static RoomCameraConfig Existing() =>
            new() { Id = "cam-1", Name = "ROOM-CAM", Host = Host, Port = 1259, Transport = ViscaTransportKind.Udp };

        private static SourceCardViewModel Tx(string mac, string name) =>
            new(new AvasDevice { DeviceId = mac, DeviceName = name, IsTransmitter = true, VendorId = 105 });

        private static CameraEditorDialog Open(
            CameraEditorViewModel vm,
            double width = 1920,
            double height = 1080,
            string? id = null)
        {
            var dialog = new CameraEditorDialog(vm, id)
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = 0,
                MaxHeight = height,
            };
            dialog.Show();
            dialog.UpdateLayout();
            Pump();
            return dialog;
        }

        private static CameraEditorViewModel NewVm(
            RoomCameraConfig? existing = null,
            IEnumerable<SourceCardViewModel>? sources = null,
            IPtzCameraFactory? factory = null,
            CameraEditorViewModel.DetectFunc? detect = null,
            IEnumerable<string>? others = null) =>
            new(existing, sources ?? Array.Empty<SourceCardViewModel>(), factory ?? new FakePtzCameraFactory(), detect, others);

        private static bool IsIconGlyph(string text) => text.All(c => c >= '' && c <= '');   // WPF-UI's own chevron font glyph

        private static void Pump() => RoomCamerasRig.Idle();

        private static T Find<T>(CameraEditorDialog d, string name) where T : FrameworkElement =>
            (T)d.FindName(name) ?? throw new InvalidOperationException("no element named " + name);

        private static void Click(Button b)
        {
            Assert.True(b.IsEnabled, $"'{b.Content}' is disabled");
            ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)!).Invoke();
            Pump();
        }

        private static void PressEscape(Window w)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w), Environment.TickCount, Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            w.RaiseEvent(args);
            Pump();
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is T t) yield return t;
                foreach (var d in Descendants<T>(c)) yield return d;
            }
        }

        [Fact]
        public void Detect_FillsPortAndTransport_FromFirstHit() => RunSta(() =>
        {
            var vm = NewVm(detect: Hits(new ProbeHit(ViscaTransportKind.Tcp, 1259, CameraPower.On)));
            vm.Host = Host;
            var d = Open(vm);
            try
            {
                Click(Find<Button>(d, "DetectButton"));

                Assert.Equal("1259", Find<Wpf.Ui.Controls.TextBox>(d, "PortBox").Text);
                Assert.True(Find<RadioButton>(d, "TcpChoice").IsChecked);
                Assert.False(Find<RadioButton>(d, "UdpChoice").IsChecked);
                Assert.Equal(ViscaTransportKind.Tcp, vm.Transport);
                Assert.Contains("1259 (TCP)", Find<TextBlock>(d, "DetectText").Text);
                Assert.Empty(Find<ItemsControl>(d, "HitList").Items.Cast<object>());
            }
            finally { d.Close(); }
        });

        [Fact]
        public void Detect_SeveralHits_AreOfferedAsButtons_AndPickingOneFillsTheFields() => RunSta(() =>
        {
            var vm = NewVm(detect: Hits(
                new ProbeHit(ViscaTransportKind.Tcp, 5678, CameraPower.On),
                new ProbeHit(ViscaTransportKind.Udp, 3001, CameraPower.On)));
            vm.Host = Host;
            var d = Open(vm);
            try
            {
                Click(Find<Button>(d, "DetectButton"));
                var list = Find<ItemsControl>(d, "HitList");
                var buttons = Descendants<Button>(list).ToList();
                Assert.Equal(new[] { "5678 (TCP)", "3001 (UDP)" }, buttons.Select(b => (string)b.Content));

                Click(buttons[1]);
                Assert.Equal("3001", Find<Wpf.Ui.Controls.TextBox>(d, "PortBox").Text);
                Assert.True(Find<RadioButton>(d, "UdpChoice").IsChecked);
            }
            finally { d.Close(); }
        });

        [Fact]
        public void Detect_NoHit_ShowsWordsAndKeepsManualEntry() => RunSta(() =>
        {
            var vm = NewVm(detect: Hits());
            vm.Host = Host;
            var d = Open(vm);
            try
            {
                Click(Find<Button>(d, "DetectButton"));

                var words = Find<TextBlock>(d, "DetectText");
                Assert.Equal("No camera answered on 192.168.1.163. Check the address, or enter the port by hand.", words.Text);
                Assert.Equal(Visibility.Visible, words.Visibility);
                Assert.True(words.ActualHeight > 0);

                var port = Find<Wpf.Ui.Controls.TextBox>(d, "PortBox");
                Assert.True(port.IsEnabled && !port.IsReadOnly);
                port.Text = "3001";
                Assert.Equal(3001, vm.Port);
                Assert.True(Find<Button>(d, "DetectButton").IsEnabled);
            }
            finally { d.Close(); }
        });

        [Fact]
        public void Detect_Cancelled_OnDialogClose_LeaksNoSocket() => RunSta(() =>
        {
            CancellationToken seen = default;
            var finished = false;
            CameraEditorViewModel.DetectFunc slow = async (_, _, _, ct) =>
            {
                seen = ct;
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return Array.Empty<ProbeHit>();
                }
                finally
                {
                    finished = true;   // the probe's sockets are disposed in this finally in ViscaProbe
                }
            };
            var vm = NewVm(detect: slow);
            vm.Host = Host;
            var d = Open(vm);

            Click(Find<Button>(d, "DetectButton"));
            Assert.True(vm.IsDetecting);
            Assert.Equal("Detecting", (string)Find<Button>(d, "DetectButton").Content);
            Assert.False(Find<Button>(d, "DetectButton").IsEnabled);
            Assert.False(seen.IsCancellationRequested);

            d.Close();
            Pump();

            Assert.True(seen.IsCancellationRequested, "closing the dialog must cancel the running detect");
            Assert.True(finished, "the detect must have run to its cleanup");
            Assert.False(vm.IsDetecting);
        });

        [Fact]
        public void Detect_Cancelled_OnDialogClose_RealProbe_LeavesThePortFree() => RunSta(() =>
        {
            // The real ViscaProbe against a silent UDP port: closing the dialog cancels it and the port can be bound again.
            using var silent = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
            var port = ((System.Net.IPEndPoint)silent.Client.LocalEndPoint!).Port;
            var vm = NewVm();
            vm.Host = "127.0.0.1";
            vm.PortText = port.ToString();
            var d = Open(vm);

            Click(Find<Button>(d, "DetectButton"));
            Assert.True(vm.IsDetecting);
            d.Close();
            for (var i = 0; i < 200 && vm.IsDetecting; i++)
            {
                Thread.Sleep(10);
                Pump();
            }
            Assert.False(vm.IsDetecting, "the detect did not stop after the dialog closed");

            silent.Dispose();
            using var rebound = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port));
        });

        [Fact]
        public void Save_Disabled_WhenHostInvalid_WithVisibleReason() => RunSta(() =>
        {
            var vm = NewVm();
            vm.Name = "Room";
            vm.Host = "   ";
            var d = Open(vm);
            try
            {
                var save = Find<Button>(d, "SaveButton");
                var reason = Find<TextBlock>(d, "ReasonText");
                Assert.False(save.IsEnabled);
                Assert.Equal("Enter the camera address", reason.Text);
                Assert.True(reason.IsVisible && reason.ActualHeight > 0 && reason.FontSize >= 12);

                Find<Wpf.Ui.Controls.TextBox>(d, "HostBox").Text = Host;
                Pump();
                Assert.True(save.IsEnabled);
                Assert.Equal(string.Empty, reason.Text);
            }
            finally { d.Close(); }
        });

        [Fact]
        public void TestConnection_Failure_ShowsWords_AndDisposesCamera() => RunSta(() =>
        {
            var fake = new FakePtzCamera { PollResult = null };
            var factory = new FakePtzCameraFactory(fake);
            var d = Open(NewVm(Existing(), factory: factory), id: "cam-1");
            try
            {
                Click(Find<Button>(d, "TestButton"));

                var words = Find<TextBlock>(d, "TestWords");
                Assert.Equal("No answer on 192.168.1.163:1259 (UDP)", words.Text);
                Assert.True(words.IsVisible);
                Assert.Equal(1, factory.CreateCount);
                Assert.Equal(1, fake.DisposeCount);
                Assert.True(Find<Button>(d, "TestButton").IsEnabled);
                // Words plus a dot, not colour alone.
                var dot = Find<System.Windows.Shapes.Ellipse>(d, "TestDot");
                Assert.True(dot.IsVisible && dot.ActualWidth > 0);
            }
            finally { d.Close(); }
        });

        [Fact]
        public void TestConnection_Success_SaysConnected_AndDisposesCamera() => RunSta(() =>
        {
            var fake = new FakePtzCamera();
            var factory = new FakePtzCameraFactory(fake);
            var d = Open(NewVm(Existing(), factory: factory), id: "cam-1");
            try
            {
                Click(Find<Button>(d, "TestButton"));
                Assert.Equal("Connected, camera on", Find<TextBlock>(d, "TestWords").Text);
                Assert.Equal(1, fake.DisposeCount);
            }
            finally { d.Close(); }
        });

        [Fact]
        public void DuplicateName_ShowsAWarningLine_WithoutBlockingSave() => RunSta(() =>
        {
            var vm = NewVm(others: new[] { "Boom cam" });
            vm.Host = Host;
            var d = Open(vm);
            try
            {
                var warning = Find<TextBlock>(d, "DuplicateWarning");
                Assert.False(warning.IsVisible);
                Find<Wpf.Ui.Controls.TextBox>(d, "NameBox").Text = "boom cam";
                Pump();
                Assert.True(warning.IsVisible);
                Assert.Contains("already named", warning.Text);
                Assert.True(Find<Button>(d, "SaveButton").IsEnabled);
            }
            finally { d.Close(); }
        });

        [Fact]
        public void Escape_Cancels_NoConfigWritten() => RunSta(() =>
        {
            var config = new RoomCamerasMemoryConfig();
            var factory = new FakePtzCameraFactory();
            var sources = new System.Collections.ObjectModel.ObservableCollection<SourceCardViewModel>();
            using var camerasVm = new RoomCamerasViewModel(config, factory, sources, () => DateTime.Now);
            var view = new HostView(d =>
            {
                Find<Wpf.Ui.Controls.TextBox>(d, "NameBox").Text = "Typed but cancelled";
                Find<Wpf.Ui.Controls.TextBox>(d, "HostBox").Text = Host;
                PressEscape(d);
            }) { DataContext = camerasVm };

            camerasVm.AddCommand.Execute(null);

            Assert.Equal(CameraEditorOutcome.Cancelled, view.Last!.Outcome);
            Assert.Null(view.Last.Result);
            Assert.Empty(config.Stored.RoomCameras);
            Assert.Empty(camerasVm.Cameras);
            Assert.Equal(0, factory.CreateCount);
        });

        [Fact]
        public void Escape_ClosesTheRemoveConfirmation_First() => RunSta(() =>
        {
            var d = Open(NewVm(Existing()), id: "cam-1");
            try
            {
                Click(Find<Button>(d, "RemoveButton"));
                Assert.True(Find<Border>(d, "RemoveConfirm").IsVisible);

                PressEscape(d);
                Assert.False(Find<Border>(d, "RemoveConfirm").IsVisible);
                Assert.True(d.IsVisible, "the first Escape only closes the confirmation");

                PressEscape(d);
                Assert.Equal(CameraEditorOutcome.Cancelled, d.Outcome);
                Assert.False(d.IsVisible);
            }
            finally { if (d.IsVisible) d.Close(); }
        });

        [Fact]
        public void EmptyTxList_StillSavesUnbound() => RunSta(() =>
        {
            var vm = NewVm();
            var d = Open(vm);
            try
            {
                var combo = Find<ComboBox>(d, "TxBox");
                Assert.Single(combo.Items);
                Assert.Equal("None", ((TxChoice)combo.SelectedItem).Name);

                Find<Wpf.Ui.Controls.TextBox>(d, "NameBox").Text = "Room";
                Find<Wpf.Ui.Controls.TextBox>(d, "HostBox").Text = Host;
                Pump();
                Click(Find<Button>(d, "SaveButton"));

                Assert.Equal(CameraEditorOutcome.Saved, d.Outcome);
                Assert.NotNull(d.Result);
                Assert.Equal(string.Empty, d.Result!.BoundTxMac);
                Assert.Equal("Room", d.Result.Name);
                Assert.Equal(Host, d.Result.Host);
            }
            finally { if (d.IsVisible) d.Close(); }
        });

        [Fact]
        public void BoundTx_ListShowsNameAndMac_AndBindsTheChoice() => RunSta(() =>
        {
            var vm = NewVm(sources: new[] { Tx("aa:bb:cc:00:00:01", "TX1_c07bb") });
            vm.Name = "Room";
            vm.Host = Host;
            var d = Open(vm);
            try
            {
                var combo = Find<ComboBox>(d, "TxBox");
                Assert.Equal(2, combo.Items.Count);
                combo.SelectedIndex = 1;
                Pump();
                Assert.Equal("aa:bb:cc:00:00:01", vm.SelectedTx.Mac);

                combo.ApplyTemplate();
                var container = (ComboBoxItem?)combo.ItemContainerGenerator.ContainerFromIndex(1);
                combo.IsDropDownOpen = true;
                Pump();
                container = (ComboBoxItem?)combo.ItemContainerGenerator.ContainerFromIndex(1);
                var texts = Descendants<TextBlock>(container!).Select(t => t.Text).ToList();
                Assert.Contains("TX1_c07bb", texts);
                Assert.Contains("aa:bb:cc:00:00:01", texts);
                combo.IsDropDownOpen = false;
            }
            finally { d.Close(); }
        });

        [Fact]
        public void Remove_AsksInline_ThenReturnsTheCameraId() => RunSta(() =>
        {
            var d = Open(NewVm(Existing()), id: "cam-1");
            try
            {
                var confirm = Find<Border>(d, "RemoveConfirm");
                Assert.False(confirm.IsVisible);
                Assert.Equal(Visibility.Visible, Find<Button>(d, "RemoveButton").Visibility);

                Click(Find<Button>(d, "RemoveButton"));
                Assert.True(confirm.IsVisible);
                Assert.Contains("ROOM-CAM", Find<TextBlock>(d, "RemoveText").Text);

                Click(Find<Button>(d, "KeepButton"));
                Assert.False(confirm.IsVisible);
                Assert.True(d.IsVisible);

                Click(Find<Button>(d, "RemoveButton"));
                Click(Find<Button>(d, "ConfirmRemoveButton"));
                Assert.Equal(CameraEditorOutcome.Removed, d.Outcome);
                Assert.Equal("cam-1", d.RemovedCameraId);
                Assert.Null(d.Result);
            }
            finally { if (d.IsVisible) d.Close(); }
        });

        [Fact]
        public void NewCamera_HasNoRemoveButton() => RunSta(() =>
        {
            var d = Open(NewVm());
            try { Assert.Equal(Visibility.Collapsed, Find<Button>(d, "RemoveButton").Visibility); }
            finally { d.Close(); }
        });

        // ---- Wiring in the Room Cameras view ----

        private sealed class HostView
        {
            public HostView(Action<CameraEditorDialog> script)
            {
                View = new RoomCamerasView();
                View.EditorPresenter = dialog =>
                {
                    Last = dialog;
                    dialog.ShowInTaskbar = false;
                    dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                    dialog.Left = -10000;
                    dialog.Top = 0;
                    dialog.Show();
                    dialog.UpdateLayout();
                    Pump();
                    script(dialog);
                    if (dialog.IsVisible) dialog.Close();
                };
            }

            public RoomCamerasView View { get; }
            public CameraEditorDialog? Last { get; private set; }
            public object? DataContext { set => View.DataContext = value; }
        }

        [Fact]
        public void TheView_OpensTheEditor_AndSavesANewCamera_ThroughTheViewModel() => RunSta(() =>
        {
            var config = new RoomCamerasMemoryConfig();
            var sources = new System.Collections.ObjectModel.ObservableCollection<SourceCardViewModel>();
            using var camerasVm = new RoomCamerasViewModel(config, new FakePtzCameraFactory(), sources, () => DateTime.Now);
            var view = new HostView(d =>
            {
                Find<Wpf.Ui.Controls.TextBox>(d, "NameBox").Text = "Boom";
                Find<Wpf.Ui.Controls.TextBox>(d, "HostBox").Text = Host;
                Pump();
                Click(Find<Button>(d, "SaveButton"));
            }) { DataContext = camerasVm };

            camerasVm.AddCommand.Execute(null);

            var saved = Assert.Single(config.Stored.RoomCameras);
            Assert.Equal("Boom", saved.Name);
            Assert.Single(camerasVm.Cameras);
        });

        [Fact]
        public void TheView_RemovesAnExistingCamera_WhenTheOperatorConfirms() => RunSta(() =>
        {
            var config = new RoomCamerasMemoryConfig();
            config.Stored.RoomCameras = new List<RoomCameraConfig> { Existing() };
            var sources = new System.Collections.ObjectModel.ObservableCollection<SourceCardViewModel>();
            using var camerasVm = new RoomCamerasViewModel(config, new FakePtzCameraFactory(), sources, () => DateTime.Now);
            var view = new HostView(d =>
            {
                Click(Find<Button>(d, "RemoveButton"));
                Click(Find<Button>(d, "ConfirmRemoveButton"));
            }) { DataContext = camerasVm };

            camerasVm.EditCommand.Execute(camerasVm.Cameras[0]);

            Assert.Empty(config.Stored.RoomCameras);
            Assert.Empty(camerasVm.Cameras);
        });

        [Fact]
        public void TheView_UpdatesAnExistingCamera_KeepingItsId() => RunSta(() =>
        {
            var config = new RoomCamerasMemoryConfig();
            config.Stored.RoomCameras = new List<RoomCameraConfig> { Existing() };
            var sources = new System.Collections.ObjectModel.ObservableCollection<SourceCardViewModel>();
            using var camerasVm = new RoomCamerasViewModel(config, new FakePtzCameraFactory(), sources, () => DateTime.Now);
            var view = new HostView(d =>
            {
                Find<Wpf.Ui.Controls.TextBox>(d, "NameBox").Text = "Renamed";
                Pump();
                Click(Find<Button>(d, "SaveButton"));
            }) { DataContext = camerasVm };

            camerasVm.EditCommand.Execute(camerasVm.Cameras[0]);

            var stored = Assert.Single(config.Stored.RoomCameras);
            Assert.Equal("cam-1", stored.Id);
            Assert.Equal("Renamed", stored.Name);
        });

        // ---- Layout ----

        [Theory]
        [InlineData(1920, 1080, false)]
        [InlineData(1920, 1080, true)]
        [InlineData(1024, 720, false)]
        [InlineData(1024, 720, true)]
        public void Renders_InBothThemes_AtFullHdAndFloor_WithoutOverflow(int w, int h, bool light) => RunSta(() =>
        {
            var themes = new ThemeService();
            themes.SetTheme(light ? ThemeMode.Light : ThemeMode.Dark);
            try
            {
                var vm = NewVm(Existing(), new[] { Tx("aa:bb:cc:00:00:01", "TX1_c07bb") }, detect: Hits(
                    new ProbeHit(ViscaTransportKind.Tcp, 5678, CameraPower.On),
                    new ProbeHit(ViscaTransportKind.Udp, 3001, CameraPower.On)));
                var d = Open(vm, w, h, "cam-1");
                try
                {
                    Click(Find<Button>(d, "DetectButton"));
                    Click(Find<Button>(d, "RemoveButton"));
                    d.UpdateLayout();

                    Assert.Equal((double)Application.Current.FindResource("Size.Camera.Dialog.Width"), d.Width);
                    Assert.True(d.ActualHeight <= h + 1, $"dialog is {d.ActualHeight}px high in {h}");
                    Assert.True(d.ActualWidth <= w);

                    var root = (FrameworkElement)VisualTreeHelper.GetChild(d, 0);
                    foreach (var name in new[] { "SaveButton", "CancelButton", "ConfirmRemoveButton", "KeepButton" })
                    {
                        var b = Find<Button>(d, name);
                        var topLeft = b.TranslatePoint(new Point(0, 0), root);
                        Assert.True(b.ActualHeight >= 36, $"{name} {b.ActualHeight}");
                        Assert.True(topLeft.Y + b.ActualHeight <= root.ActualHeight + 1, $"{name} is below the window");
                        Assert.True(topLeft.X + b.ActualWidth <= root.ActualWidth + 1, $"{name} is right of the window");
                    }
                    var texts = Descendants<TextBlock>(d).Where(t => t.IsVisible && !string.IsNullOrWhiteSpace(t.Text) && t.TemplatedParent == null && !IsIconGlyph(t.Text)).ToList();
                    Assert.All(texts, t => Assert.True(t.FontSize >= 12, $"'{t.Text}' is {t.FontSize}px"));
                    Assert.Equal(Visibility.Visible, Find<Border>(d, "RemoveConfirm").Visibility);
                }
                finally { d.Close(); }
            }
            finally { themes.SetTheme(ThemeMode.Dark); }
        });
    }
}
