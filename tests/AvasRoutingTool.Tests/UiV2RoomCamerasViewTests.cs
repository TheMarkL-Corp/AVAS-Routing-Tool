using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
    /// <summary>In-memory config file for the Room Cameras view tests.</summary>
    internal sealed class RoomCamerasMemoryConfig : IConfigService
    {
        public AppConfig Stored { get; set; } = new();
        public AppConfig Load() => Stored;
        public void Save(AppConfig config) => Stored = config;
    }

    /// <summary>
    /// A Room Cameras view hosted in an off-screen window of a given size, with fake cameras and an in-memory config.
    /// Shared by the STA view tests and the screenshot helper.
    /// </summary>
    internal sealed class RoomCamerasRig : IDisposable
    {
        public const string Mac = "aa:bb:cc:00:00:01";

        public RoomCamerasMemoryConfig Config { get; } = new();
        public FakePtzCameraFactory Factory { get; } = new();
        public ObservableCollection<SourceCardViewModel> Sources { get; } = new();
        public DateTime Clock { get; set; } = new DateTime(2026, 11, 2, 10, 0, 0);
        public RoomCamerasViewModel Vm { get; }
        public RoomCamerasView View { get; }
        public Window Window { get; }
        public List<RoomCameraViewModel?> EditorRequests { get; } = new();

        public RoomCamerasRig(double width, double height, IEnumerable<RoomCameraConfig> cameras, IEnumerable<SourceCardViewModel>? cards = null)
        {
            foreach (var card in cards ?? Enumerable.Empty<SourceCardViewModel>()) Sources.Add(card);
            Config.Stored.RoomCameras = cameras.ToList();
            Vm = new RoomCamerasViewModel(Config, Factory, Sources, () => Clock);
            View = new RoomCamerasView { DataContext = Vm, Width = width, Height = height };
            View.EditorRequested += camera => EditorRequests.Add(camera);
            Window = new Window { Content = View, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000, Top = 0 };
            Window.Show();
            Relayout();
        }

        public static RoomCameraConfig Cam(string id = "a", string name = "ROOM-CAM", string mac = "") =>
            new() { Id = id, Name = name, Host = "192.168.1.163", Port = 1259, BoundTxMac = mac };

        public static SourceCardViewModel Card(string name = "TX1_c07bb", bool multicast = true, bool active = true, string mac = Mac)
        {
            var device = new AvasDevice { DeviceId = mac, DeviceName = name, VendorId = 105, IsTransmitter = true, IsActive = active };
            if (multicast) device.MulticastIp = "239.1.1.1";
            return new SourceCardViewModel(device);
        }

        public FakePtzCamera Fake(int index = 0) => Factory.Created[index];

        public void Relayout()
        {
            View.UpdateLayout();
            Idle();
            View.UpdateLayout();
        }

        public static void Idle()
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }

        public T Named<T>(string name) where T : FrameworkElement => All<FrameworkElement>(View).OfType<T>().First(e => e.Name == name);

        public static List<T> All<T>(DependencyObject root) where T : DependencyObject
        {
            var found = new List<T>();
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) found.Add(match);
                found.AddRange(All<T>(child));
            }
            return found;
        }

        /// <summary>The bounds of an element in the view's coordinates.</summary>
        public Rect BoundsOf(FrameworkElement element) => new(element.TranslatePoint(new Point(0, 0), View), new Size(element.ActualWidth, element.ActualHeight));

        public void Dispose()
        {
            Window.Close();
            Vm.Dispose();
        }
    }

    /// <summary>
    /// The Room Cameras view (RC11): layout at 1920x1080 and 1024x720 in both themes, the picture never covered, the five picture states,
    /// truncation, Stop on hide, the keyboard, Lock and theme repaint.
    /// </summary>
    [Collection("WpfSta")]
    public class UiV2RoomCamerasViewTests
    {
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
            if (failure != null) throw new Xunit.Sdk.XunitException("Room cameras view failure: " + failure);
        }

        private static void WithTheme(bool light, Action body)
        {
            if (light) new ThemeService().SetTheme(ThemeMode.Light);
            try { body(); }
            finally { if (light) new ThemeService().SetTheme(ThemeMode.Dark); }
        }

        private static RoomCamerasRig TwoCameras(double width, double height) =>
            new(width, height, new[] { RoomCamerasRig.Cam("a", "ROOM-CAM", RoomCamerasRig.Mac), RoomCamerasRig.Cam("b", "OR-2 ceiling") },
                new[] { RoomCamerasRig.Card() });

        private static void MakeLive(SourceCardViewModel card)
        {
            card.PreviewBitmap = new WriteableBitmap(64, 36, 96, 96, PixelFormats.Bgr24, null);
            card.IsStreaming = true;
        }

        private static bool Pumped(Func<bool> condition)
        {
            var until = DateTime.UtcNow.AddSeconds(3);
            while (!condition() && DateTime.UtcNow < until)
            {
                RoomCamerasRig.Idle();
                Thread.Sleep(10);
            }
            return condition();
        }

        // ---- Layout ----

        [Theory]
        [InlineData(1920, 1080, 1500, false)]
        [InlineData(1920, 1080, 1500, true)]
        [InlineData(1024, 720, 680, false)]
        [InlineData(1024, 720, 680, true)]
        public void Renders_AtFullHdAndFloor_NoOverflow_Dark_And_Light(double width, double height, double minPicture, bool light) =>
            RunSta(() => WithTheme(light, () =>
            {
                using var rig = TwoCameras(width, height);
                MakeLive(rig.Sources[0]);
                rig.Relayout();

                var picture = rig.Named<Border>("PictureArea");
                var rail = rig.Named<Border>("Rail");
                Assert.True(picture.ActualWidth >= minPicture, $"picture is {picture.ActualWidth} px wide at {width}x{height}");
                Assert.InRange(rail.ActualWidth, 300, 360);

                var view = new Rect(0, 0, rig.View.ActualWidth, rig.View.ActualHeight);
                Assert.Equal(width, rig.View.ActualWidth, 1);
                Assert.Equal(height, rig.View.ActualHeight, 1);
                foreach (var name in new[] { "ChipsRow", "PictureArea", "Rail", "CaptionRow" })
                {
                    var bounds = rig.BoundsOf(rig.Named<FrameworkElement>(name));
                    Assert.True(bounds.Left >= -0.5 && bounds.Top >= -0.5 && bounds.Right <= view.Right + 0.5 && bounds.Bottom <= view.Bottom + 0.5,
                        $"{name} {bounds} overflows the view {view} at {width}x{height}");
                }

                // The picture and the rail do not overlap, and the page itself never scrolls sideways.
                Assert.True(rig.BoundsOf(picture).Right <= rig.BoundsOf(rail).Left + 0.5);
                var pictureRatio = picture.ActualWidth / picture.ActualHeight;
                Assert.True(pictureRatio > 1, "the picture hero is landscape");

                // At the floor the pad, speed and zoom stay in view without scrolling the rail.
                var scroller = rig.Named<ScrollViewer>("RailScroller");
                var zoom = rig.Named<Slider>("ZoomSlider");
                var zoomBottom = zoom.TranslatePoint(new Point(0, zoom.ActualHeight), scroller).Y;
                Assert.True(zoomBottom <= scroller.ActualHeight + 0.5, $"zoom slider ends at {zoomBottom}, the rail shows {scroller.ActualHeight} at {width}x{height}");
                Assert.Equal(0, scroller.VerticalOffset);
            }));

        [Fact]
        public void PictureIsNotCoveredByAnyControl() =>
            RunSta(() =>
            {
                using var rig = TwoCameras(1024, 720);
                MakeLive(rig.Sources[0]);
                var camera = rig.Vm.Cameras[0];

                // Drive the Stop-not-confirmed banner on as well: it must take its own row, not sit on the picture.
                rig.Fake().FailNextStops(20);
                camera.BeginMove(AvasRoutingTool.Services.Visca.PanTiltDirection.Up);
                camera.EndMove();
                for (var i = 0; i < 12 && camera.MotionState != MotionState.StopNotConfirmed; i++)
                {
                    rig.Clock = rig.Clock.AddSeconds(3);
                    camera.Tick();
                    RoomCamerasRig.Idle();
                }
                rig.Relayout();
                var banner = rig.Named<Border>("StopBanner");
                Assert.Equal(MotionState.StopNotConfirmed, camera.MotionState);
                Assert.True(banner.IsVisible, "the Stop-not-confirmed banner is shown");

                var picture = rig.Named<Border>("PictureArea");
                Assert.True(rig.Named<Image>("LiveImage").IsVisible);
                var pictureBounds = rig.BoundsOf(picture);
                Assert.True(pictureBounds.Width > 0 && pictureBounds.Height > 0);

                var ancestors = new HashSet<DependencyObject>();
                for (DependencyObject? d = picture; d != null; d = VisualTreeHelper.GetParent(d)) ancestors.Add(d);
                var inside = new HashSet<DependencyObject>(RoomCamerasRig.All<DependencyObject>(picture));

                var offenders = RoomCamerasRig.All<FrameworkElement>(rig.View)
                    .Where(e => (e is Control || e is TextBlock) && e.IsVisible && e.ActualWidth > 0 && e.ActualHeight > 0)
                    .Where(e => !ancestors.Contains(e) && !inside.Contains(e))
                    .Where(e => rig.BoundsOf(e).IntersectsWith(Shrink(pictureBounds)))
                    .Select(e => e.GetType().Name + ":" + (string.IsNullOrEmpty(e.Name) ? AutomationProperties.GetName(e) : e.Name))
                    .ToList();
                Assert.True(offenders.Count == 0, "controls over the picture: " + string.Join(", ", offenders));

                // Nothing interactive sits inside the picture while it shows video.
                var interactive = RoomCamerasRig.All<Control>(picture).Where(c => c.IsVisible && c is ButtonBase or Slider).ToList();
                Assert.Empty(interactive);
            });

        [Fact]
        public void UnreachableCamera_LinkDots_AreNotTheConnectedColour() =>
            RunSta(() =>
            {
                using var rig = TwoCameras(1920, 1080);
                rig.Fake(1).LinkState = CameraLinkState.Unreachable;
                rig.Vm.Selected = rig.Vm.Cameras[1];
                rig.Relayout();

                var chips = RoomCamerasRig.All<RadioButton>(rig.View).Where(r => r.Tag is bool).ToList();
                Assert.Equal(2, chips.Count);
                var connectedChipDot = RoomCamerasRig.All<System.Windows.Shapes.Ellipse>(chips[0]).Single(e => e.Name == "StatusDot");
                var unreachableChipDot = RoomCamerasRig.All<System.Windows.Shapes.Ellipse>(chips[1]).Single(e => e.Name == "StatusDot");
                var railDot = rig.Named<System.Windows.Shapes.Ellipse>("LinkDot");

                var connected = ((SolidColorBrush)connectedChipDot.Fill).Color;
                var unreachable = ((SolidColorBrush)unreachableChipDot.Fill).Color;
                Assert.NotEqual(connected, unreachable);
                Assert.Equal(unreachable, ((SolidColorBrush)railDot.Fill).Color);
            });

        private static void Click(Button button)
        {
            Assert.True(button.IsEnabled && button.Command.CanExecute(button.CommandParameter), "the button can be pressed");
            button.Command.Execute(button.CommandParameter);
        }

        private static Rect Shrink(Rect r) => new(r.X + 1, r.Y + 1, Math.Max(0, r.Width - 2), Math.Max(0, r.Height - 2));

        // ---- States ----

        [Fact]
        public void EmptyConfig_ShowsAddCameraEmptyState() =>
            RunSta(() =>
            {
                using var rig = new RoomCamerasRig(1024, 720, Array.Empty<RoomCameraConfig>());
                var empty = rig.Named<StackPanel>("EmptyState");
                var add = rig.Named<Button>("EmptyAddButton");
                Assert.True(empty.IsVisible);
                Assert.True(add.IsVisible);
                Assert.Equal("Add a camera", add.Content);
                Assert.False(rig.Named<Grid>("ChipsRow").IsVisible);
                Assert.False(rig.Named<Grid>("Body").IsVisible);

                Click(add);
                Assert.Single(rig.EditorRequests);
                Assert.Null(rig.EditorRequests[0]);
            });

        [Fact]
        public void FourPreviewStates_ShowTheirNextAction() =>
            RunSta(() =>
            {
                var card = RoomCamerasRig.Card();
                using var rig = new RoomCamerasRig(1920, 1080,
                    new[] { RoomCamerasRig.Cam("unbound", "NO-SRC"), RoomCamerasRig.Cam("offline", "GONE", "aa:bb:cc:00:00:99"), RoomCamerasRig.Cam("bound", "ROOM-CAM", RoomCamerasRig.Mac) },
                    new[] { card });
                var unbound = rig.Vm.Cameras[0];
                var offline = rig.Vm.Cameras[1];
                var bound = rig.Vm.Cameras[2];

                // No source bound: "Bind a source" raises the editor for this camera.
                rig.Vm.Selected = unbound;
                rig.Relayout();
                Assert.Equal(CameraPreviewState.NoSourceBound, unbound.Preview);
                var bind = rig.Named<Button>("BindSourceButton");
                Assert.True(bind.IsVisible);
                Assert.Contains("No source bound to NO-SRC", AllTexts(rig.Named<StackPanel>("StateNoSource")));
                Click(bind);
                Assert.Same(unbound, Assert.Single(rig.EditorRequests));

                // Source offline: a message, no action (it resolves by itself).
                rig.Vm.Selected = offline;
                rig.Relayout();
                Assert.Equal(CameraPreviewState.SourceOffline, offline.Preview);
                Assert.True(rig.Named<StackPanel>("StateOffline").IsVisible);
                Assert.False(rig.Named<StackPanel>("StateNoSource").IsVisible);
                Assert.Empty(RoomCamerasRig.All<Button>(rig.Named<StackPanel>("StateOffline")));

                // Standby: the primary Start preview action.
                rig.Vm.Selected = bound;
                rig.Relayout();
                Assert.Equal(CameraPreviewState.Standby, bound.Preview);
                var start = rig.Named<Button>("StartPreviewButton");
                Assert.True(start.IsVisible);
                Assert.True(start.IsEnabled);
                Assert.Equal("Start preview", start.Content);
                Assert.False(rig.Named<TextBlock>("StartReason").IsVisible);

                // Starting: a progress message.
                card.IsRefreshing = true;
                rig.Relayout();
                Assert.Equal(CameraPreviewState.Starting, bound.Preview);
                Assert.True(rig.Named<StackPanel>("StateStarting").IsVisible);
                Assert.Contains("Starting preview...", AllTexts(rig.Named<StackPanel>("StateStarting")));
                card.IsRefreshing = false;

                // Live: the image fills the picture area, uniform on the dark background.
                MakeLive(card);
                rig.Relayout();
                var image = rig.Named<Image>("LiveImage");
                Assert.Equal(CameraPreviewState.Live, bound.Preview);
                Assert.NotNull(image.Source);
                Assert.Equal(Stretch.Uniform, image.Stretch);
                Assert.False(rig.Named<StackPanel>("StateStandby").IsVisible);
                Assert.Equal("Brush.Background.Dark", ResourceKeyOfBackground(rig.Named<Border>("PictureArea")));
            });

        [Fact]
        public void StandbyWithoutMulticast_ShowsWhyStartIsUnavailable() =>
            RunSta(() =>
            {
                var card = RoomCamerasRig.Card(multicast: false);
                using var rig = new RoomCamerasRig(1024, 720, new[] { RoomCamerasRig.Cam("a", "ROOM-CAM", RoomCamerasRig.Mac) }, new[] { card });
                var reason = rig.Named<TextBlock>("StartReason");
                Assert.True(reason.IsVisible);
                Assert.Equal("No multicast address", reason.Text);
                Assert.False(rig.Named<Button>("StartPreviewButton").IsEnabled);
            });

        private static string ResourceKeyOfBackground(Border border)
        {
            var expected = (SolidColorBrush)Application.Current.FindResource("Brush.Background.Dark");
            return border.Background is SolidColorBrush b && b.Color == expected.Color ? "Brush.Background.Dark" : border.Background?.ToString() ?? "null";
        }

        private static IEnumerable<string> AllTexts(DependencyObject root) => RoomCamerasRig.All<TextBlock>(root).Select(t => t.Text);

        // ---- Truncation ----

        [Fact]
        public void LongNames_Truncate_WithTooltip() =>
            RunSta(() =>
            {
                var longName = "Operating room number 2 ceiling mounted PTZ camera north";
                var longSource = "TX7_unit_with_a_remarkably_long_source_name_c07bb";
                Assert.True(longName.Length >= 40 && longSource.Length >= 40);
                using var rig = new RoomCamerasRig(1024, 720,
                    new[] { RoomCamerasRig.Cam("a", longName, RoomCamerasRig.Mac) },
                    new[] { RoomCamerasRig.Card(longSource) });
                MakeLive(rig.Sources[0]);
                rig.Relayout();

                var maxName = (double)Application.Current.FindResource("Size.Camera.Chip.Name.Max");
                var chipText = RoomCamerasRig.All<TextBlock>(rig.Named<ScrollViewer>("ChipsScroller")).First(t => t.Text == longName);
                Assert.Equal(TextTrimming.CharacterEllipsis, chipText.TextTrimming);
                Assert.True(chipText.ActualWidth <= maxName + 0.5, $"chip name is {chipText.ActualWidth} px wide");
                Assert.Equal(longName, chipText.ToolTip);
                Assert.Equal(longName, RoomCamerasRig.All<RadioButton>(rig.Named<ScrollViewer>("ChipsScroller")).Single().ToolTip);

                var caption = rig.Named<TextBlock>("CaptionText");
                var full = rig.Vm.Cameras[0].CaptionText;
                Assert.True(full.Length >= 40);
                Assert.Equal(TextTrimming.CharacterEllipsis, caption.TextTrimming);
                Assert.Equal(full, caption.ToolTip);
                Assert.True(rig.BoundsOf(caption).Right <= rig.BoundsOf(rig.Named<Border>("PictureArea")).Right + 0.5, "the caption stays inside the picture column");

                var railName = rig.Named<TextBlock>("RailName");
                Assert.Equal(TextTrimming.CharacterEllipsis, railName.TextTrimming);
                Assert.Equal(longName, railName.ToolTip);
                Assert.True(rig.BoundsOf(railName).Right <= rig.BoundsOf(rig.Named<Border>("Rail")).Right + 0.5);
            });

        // ---- Safety ----

        [Fact]
        public void HidingTheView_SendsStop() =>
            RunSta(() =>
            {
                using var rig = TwoCameras(1024, 720);
                rig.Vm.Cameras[0].BeginMove(AvasRoutingTool.Services.Visca.PanTiltDirection.Left);
                Assert.Contains(rig.Fake().Calls, c => c.StartsWith("Move:Left", StringComparison.Ordinal));
                Assert.DoesNotContain("StopMove", rig.Fake().Calls);

                rig.View.Visibility = Visibility.Collapsed;
                rig.Relayout();

                Assert.True(Pumped(() => rig.Fake().Calls.Contains("StopMove")), "hiding the view must send Stop: " + string.Join(",", rig.Fake().Calls));
            });

        [Fact]
        public void ArrowKeyUp_EndsMotion() =>
            RunSta(() =>
            {
                using var rig = TwoCameras(1024, 720);
                FocusView(rig);

                RaiseKey(rig, Keyboard.PreviewKeyDownEvent, Key.Up);
                Assert.Contains(rig.Fake().Calls, c => c.StartsWith("Move:Up:", StringComparison.Ordinal));
                Assert.DoesNotContain("StopMove", rig.Fake().Calls);

                RaiseKey(rig, Keyboard.PreviewKeyUpEvent, Key.Up);
                Assert.True(Pumped(() => rig.Fake().Calls.Contains("StopMove")), "key up must send Stop");
            });

        [Fact]
        public void TwoArrows_MakeADiagonal_AndEscapeStops() =>
            RunSta(() =>
            {
                using var rig = TwoCameras(1024, 720);
                FocusView(rig);

                RaiseKey(rig, Keyboard.PreviewKeyDownEvent, Key.Up);
                RaiseKey(rig, Keyboard.PreviewKeyDownEvent, Key.Left);
                Assert.Contains(rig.Fake().Calls, c => c.StartsWith("Move:UpLeft:", StringComparison.Ordinal));

                RaiseKey(rig, Keyboard.PreviewKeyDownEvent, Key.Escape);
                Assert.True(Pumped(() => rig.Fake().Calls.Contains("StopMove")), "Esc must send Stop");
            });

        [Fact]
        public void Keys_AreIgnored_WhenFocusIsOutsideTheView() =>
            RunSta(() =>
            {
                using var rig = TwoCameras(1024, 720);
                Keyboard.ClearFocus();
                rig.Relayout();
                Assert.False(rig.View.IsKeyboardFocusWithin);

                RaiseKey(rig, Keyboard.PreviewKeyDownEvent, Key.Up);
                Assert.DoesNotContain(rig.Fake().Calls, c => c.StartsWith("Move", StringComparison.Ordinal));
            });

        private static void FocusView(RoomCamerasRig rig)
        {
            rig.Window.Activate();
            rig.View.Focus();
            Keyboard.Focus(rig.View);
            rig.Relayout();
            Assert.True(rig.View.IsKeyboardFocusWithin, "the test view must have keyboard focus");
        }

        private static void RaiseKey(RoomCamerasRig rig, RoutedEvent routedEvent, Key key)
        {
            var source = PresentationSource.FromVisual(rig.View);
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = routedEvent };
            rig.View.RaiseEvent(args);
        }

        // ---- Lock ----

        [Fact]
        public void Locked_DisablesPadSlidersPower() =>
            RunSta(() =>
            {
                using var rig = TwoCameras(1920, 1080);
                var pad = rig.Named<PtzPadView>("Pad");
                var keys = RoomCamerasRig.All<Button>(pad);
                Assert.Equal(9, keys.Count);
                Assert.All(keys, k => Assert.True(k.IsEnabled));
                Assert.True(rig.Named<Slider>("ZoomSlider").IsEnabled);
                Assert.True(rig.Named<Slider>("FocusSlider").IsEnabled);
                Assert.True(rig.Named<Button>("PowerButton").IsEnabled);

                rig.Vm.Cameras[0].IsLocked = true;
                rig.Relayout();
                CommandManager.InvalidateRequerySuggested();
                rig.Relayout();

                Assert.All(keys, k => Assert.False(k.IsEnabled, AutomationProperties.GetName(k)));
                Assert.False(rig.Named<Slider>("ZoomSlider").IsEnabled);
                Assert.False(rig.Named<Slider>("FocusSlider").IsEnabled);
                Assert.False(rig.Named<Button>("PowerButton").IsEnabled);
                Assert.False(rig.Named<FrameworkElement>("AutoFocusSwitch").IsEnabled);
                Assert.False(rig.Named<Grid>("SpeedSegment").IsEnabled);
                Assert.True(rig.Named<FrameworkElement>("LockSwitch").IsEnabled, "Lock itself stays usable so one tap unlocks");

                rig.Vm.Cameras[0].IsLocked = false;
                rig.Relayout();
                CommandManager.InvalidateRequerySuggested();
                rig.Relayout();
                Assert.All(keys, k => Assert.True(k.IsEnabled));
            });

        [Fact]
        public void AutoFocus_DisablesTheFocusSlider() =>
            RunSta(() =>
            {
                using var rig = TwoCameras(1920, 1080);
                rig.Vm.Cameras[0].AutoFocus = true;
                rig.Relayout();
                Assert.False(rig.Named<Slider>("FocusSlider").IsEnabled);
                Assert.True(rig.Named<Slider>("ZoomSlider").IsEnabled);
            });

        // ---- Theme ----

        [Fact]
        public void DarkLightSwitch_RepaintsRail() =>
            RunSta(() =>
            {
                var themes = new ThemeService();
                try
                {
                    themes.SetTheme(ThemeMode.Dark);
                    using var rig = TwoCameras(1920, 1080);
                    var rail = rig.Named<Border>("Rail");
                    var name = rig.Named<TextBlock>("RailName");
                    var darkRail = ((SolidColorBrush)rail.Background).Color;
                    var darkText = ((SolidColorBrush)name.Foreground).Color;
                    var darkPixel = PixelAt(rig.View, rig.BoundsOf(rail).X + rail.ActualWidth / 2, rig.BoundsOf(rail).Y + 3);

                    themes.SetTheme(ThemeMode.Light);
                    rig.Relayout();
                    var lightRail = ((SolidColorBrush)rail.Background).Color;
                    var lightText = ((SolidColorBrush)name.Foreground).Color;
                    var lightPixel = PixelAt(rig.View, rig.BoundsOf(rail).X + rail.ActualWidth / 2, rig.BoundsOf(rail).Y + 3);

                    Assert.NotEqual(darkRail, lightRail);
                    Assert.NotEqual(darkText, lightText);
                    Assert.NotEqual(darkPixel, lightPixel);
                    // The rail text is readable on the rail: dark text on light, light on dark.
                    Assert.True(Luminance(lightText) < Luminance(lightRail));
                    Assert.True(Luminance(darkText) > Luminance(darkRail));
                }
                finally
                {
                    themes.SetTheme(ThemeMode.Dark);
                }
            });

        private static double Luminance(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

        private static Color PixelAt(FrameworkElement element, double px, double py)
        {
            int x = (int)px, y = (int)py;
            element.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
        }

        // ---- Spoken names ----

        [Fact]
        public void EveryControl_HasASpokenName() =>
            RunSta(() =>
            {
                using var rig = TwoCameras(1920, 1080);
                var controls = RoomCamerasRig.All<Control>(rig.View)
                    .Where(c => c.IsVisible && (c is Button || c is ToggleButton || c is Slider || c is Wpf.Ui.Controls.ToggleSwitch))
                    .Where(c => c.GetType().Namespace != "System.Windows.Controls.Primitives" || c is ToggleButton)
                    .Where(c => !(c is System.Windows.Controls.Primitives.RepeatButton) && !(c is System.Windows.Controls.Primitives.Thumb))
                    .ToList();
                Assert.NotEmpty(controls);
                var unnamed = controls.Where(c => string.IsNullOrWhiteSpace(AutomationProperties.GetName(c))).Select(c => c.GetType().Name + ":" + c.Name).ToList();
                Assert.True(unnamed.Count == 0, "controls without an AutomationProperties.Name: " + string.Join(", ", unnamed));
            });
    }
}
