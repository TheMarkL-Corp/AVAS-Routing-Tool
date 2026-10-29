using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Instantiates the real XAML views with real view models and forces a layout pass.
    /// Catches binding/resource crashes (e.g. TwoWay binding on a read-only property) that
    /// pure view-model tests can never see.
    /// </summary>
    [Collection("WpfSta")]
    public partial class ViewSmokeTests
    {
        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    // The input mode is process-wide: every test starts as a pointer user (the start state) and sets what it needs.
                    InputModality.IsKeyboard = false;
                    var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    foreach (var path in new[] { "Themes/Theme.Dark", "Theme", "Icons" })
                    {
                        app.Resources.MergedDictionaries.Add(new ResourceDictionary
                        {
                            Source = new Uri($"pack://application:,,,/AvasRoutingTool;component/Resources/{path}.xaml")
                        });
                    }
                    body();
                }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("View smoke failure: " + failure);
        }

        private static void Layout(FrameworkElement el)
        {
            var window = new Window { Content = el, Width = 1920, Height = 1080, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000 };
            window.Show();
            el.Measure(new Size(1920, 1080));
            el.Arrange(new Rect(0, 0, 1920, 1080));
            el.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            window.Close();
        }

        private static AvasDevice Rx(string src = "") => new()
        {
            DeviceId = "74fe483c059e", DeviceName = "RX1", IsReceiver = true, IsActive = true,
            ActiveSourceId = src, ActiveSourceName = src == "" ? "" : "TX2"
        };

        private static DisplayCardViewModel MultiViewDisplay(string layoutId, bool fill)
        {
            var vm = new DisplayCardViewModel(Rx());
            vm.ApplyLayout(Sdvoe.MultiView.MultiViewPresets.Build(layoutId, 1920, 1080), layoutId.ToUpperInvariant());
            if (fill)
            {
                var src = new SourceCardViewModel(new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = "TX2", VendorId = 105, IsTransmitter = true, IsActive = true })
                {
                    PreviewBitmap = new System.Windows.Media.Imaging.WriteableBitmap(64, 36, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null)
                };
                vm.AssignSlot(vm.Slots[0].Index, src);
                vm.AssignSlot(vm.Slots[1].Index, new SourceCardViewModel(new AvasDevice { DeviceId = "0016c0589c1e", DeviceName = "VEGA", VendorId = 192, IsTransmitter = true, IsActive = true }));
            }
            return vm;
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                var nested = FindDescendant<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private static System.Windows.Controls.TextBlock? FindTextBlock(DependencyObject root, string startsWith)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is System.Windows.Controls.TextBlock tb && tb.Text.StartsWith(startsWith, StringComparison.Ordinal)) return tb;
                var nested = FindTextBlock(child, startsWith);
                if (nested != null) return nested;
            }
            return null;
        }

        // ---- MultiView Composer (Task 10a) ----
        // NOTE: test names here must not contain the substring "Live" (the shared filter FullyQualifiedName!~Live).

        /// <summary>Runs an async view-model call to completion on this STA thread, pumping the dispatcher meanwhile.</summary>
        private static void Pump(Func<Task> work)
        {
            var task = work();
            if (!task.IsCompleted)
            {
                var frame = new DispatcherFrame();
                task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
            }
            task.GetAwaiter().GetResult();
        }

        private static void Idle()
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }

        /// <summary>Shows the view in a 1920x1080 window, keeps it open while <paramref name="assert"/> runs (IsVisible needs it).</summary>
        private static void Showing(FrameworkElement view, Action assert)
        {
            var window = new Window { Content = view, Width = 1920, Height = 1080, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000 };
            window.Show();
            try
            {
                view.UpdateLayout();
                Idle();
                assert();
            }
            finally
            {
                window.Close();
            }
        }

        private static void Relayout(FrameworkElement view)
        {
            view.UpdateLayout();
            Idle();
            view.UpdateLayout();
        }

        private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            var found = new List<T>();
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T match) found.Add(match);
                found.AddRange(FindAll<T>(child));
            }
            return found;
        }

        private static List<FrameworkElement> Named(DependencyObject root, string name) =>
            FindAll<FrameworkElement>(root).Where(e => e.Name == name).ToList();

        private static List<TextBlock> VisibleTexts(DependencyObject root, string startsWith) =>
            FindAll<TextBlock>(root).Where(t => t.IsVisible && t.Text.StartsWith(startsWith, StringComparison.Ordinal)).ToList();

        private static void AssertVisibleText(DependencyObject root, string startsWith) =>
            Assert.True(VisibleTexts(root, startsWith).Count > 0, $"no visible text starting with '{startsWith}'");

        private static WriteableBitmap Picture() => new(64, 36, 96, 96, PixelFormats.Bgr24, null);

        private sealed class ComposerRig
        {
            public MockSdvoeClient Client { get; } = new();
            public MultiViewOrchestrator Mv { get; }
            public DisplayCardViewModel Rx { get; }
            public SourceCardViewModel Tx1 { get; } = Src("74fe488b07bb", "TX1", 105);
            public SourceCardViewModel Tx2 { get; } = Src("74fe488b07bc", "TX2", 105);
            public SourceCardViewModel Vega { get; } = Src("0016c0589c1e", "VEGA_1100", 192);
            public List<SourceCardViewModel> Sources { get; }

            public ComposerRig()
            {
                Mv = new MultiViewOrchestrator(Client);
                Rx = new DisplayCardViewModel(new AvasDevice { DeviceId = "74fe483c059e", DeviceName = "RX1_c059e_SFPA", IsReceiver = true, IsActive = true });
                Sources = new List<SourceCardViewModel> { Tx1, Tx2, Vega };
            }

            public MultiViewComposerViewModel Composer(string? layoutId = null, Func<bool>? animationsEnabled = null)
            {
                var c = new MultiViewComposerViewModel(Rx, Mv, () => Sources, LayoutItemViewModel.CreateCatalog(), null, null, animationsEnabled);
                if (layoutId != null) Pump(() => c.SelectLayoutAsync(layoutId));
                return c;
            }

            public MultiViewComposerView View(MultiViewComposerViewModel c) => new() { DataContext = c, Sources = Sources };

            private static SourceCardViewModel Src(string mac, string name, int vendor) =>
                new(new AvasDevice { DeviceId = mac, DeviceName = name, VendorId = vendor, IsTransmitter = true, IsActive = true });
        }

        // ---- Task 10a fix round 1 ----

        /// <summary>Lays the view out at exactly <paramref name="width"/> x <paramref name="height"/> (the content area of a real window).</summary>
        private static void ShowingAt(FrameworkElement view, double width, double height, Action assert)
        {
            view.Width = width;
            view.Height = height;
            var window = new Window { Content = view, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Left = -10000 };
            window.Show();
            try
            {
                Relayout(view);
                assert();
            }
            finally
            {
                window.Close();
            }
        }

        private static Rect BoundsIn(FrameworkElement el, Visual ancestor) =>
            el.TransformToAncestor(ancestor).TransformBounds(new Rect(el.RenderSize));

        private static void Wait(TimeSpan time)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = time };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static void Tap(UIElement el, RoutedEvent routedEvent) =>
            el.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = routedEvent, Source = el });

        private static void Click(FrameworkElement button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        private static (MainViewModel Vm, DisplayCardViewModel Rx, SourceCardViewModel[] Sources) Overview(Func<DateTime>? clock = null)
        {
            var client = new MockSdvoeClient();
            var vm = new MainViewModel(new Configuration.ConfigService(), client, new MockRtpReceiver(), new MulticastIpManager(),
                new RoutingOrchestrator(client), new MultiViewOrchestrator(client), clock, post: a => a());
            var sources = new[]
            {
                new SourceCardViewModel(new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = "TX1_c07bb_SFPA", VendorId = 105, IsTransmitter = true, IsActive = true }) { PreviewBitmap = Picture() },
                new SourceCardViewModel(new AvasDevice { DeviceId = "74fe488b07bc", DeviceName = "TX2_DUAL LINK_SFPA", VendorId = 105, LinkMode = "Dual Link", IsTransmitter = true, IsActive = true }) { PreviewBitmap = Picture() },
                new SourceCardViewModel(new AvasDevice { DeviceId = "0016c0589c1e", DeviceName = "VEGA_1100", VendorId = 192, IsTransmitter = true, IsActive = true }),
                new SourceCardViewModel(new AvasDevice { DeviceId = "74fe488b07bd", DeviceName = "TX3_c07bd_SFPA", VendorId = 105, IsTransmitter = true, IsActive = false }),
            };
            foreach (var s in sources) vm.Sources.Add(s);
            var rx = new DisplayCardViewModel(new AvasDevice { DeviceId = "74fe483c059e", DeviceName = "RX1_c059e_SFPA", IsReceiver = true, IsActive = true });
            vm.Displays.Add(rx);
            return (vm, rx, sources);
        }

        /// <summary>
        /// A ScrollViewer with the theme's implicit style, set explicitly: implicit app-level styles are not looked up
        /// from this test thread (it is not the thread that created the Application).
        /// </summary>
        private static ScrollViewer ThemedScroll(double contentHeight) => new()
        {
            Style = (Style)Application.Current.FindResource(typeof(ScrollViewer)),
            Height = 200, Width = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Height = contentHeight },
        };

        private static double Luminance(Color c)
        {
            static double Channel(byte v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        private static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        // ---- Task 10b: the display card (Layout menu, drop choice, draft chip, Copy layout, copy target states, Undo toast) ----

        private sealed class CopyRig
        {
            public const string Rx1Mac = "74fe483c059e";
            public const string PaxMac = "74fe4800355a";
            public const string Rx3Mac = "74fe48000003";
            public const string Tx2Mac = "74fe488b07bb";
            public const string VegaMac = "74fe48000001";

            public MockSdvoeClient Client { get; } = new();
            public MainViewModel Vm { get; }
            public DisplayCardViewModel Rx1 { get; }
            public DisplayCardViewModel Pax { get; }
            public DisplayCardViewModel Rx3 { get; }

            public SourceCardViewModel Tx2 { get; } = new(new AvasDevice { DeviceId = Tx2Mac, DeviceName = "TX2_DUAL LINK_SFPA", VendorId = 105, LinkMode = "Dual Link", IsTransmitter = true, IsActive = true }) { PreviewBitmap = Picture() };
            public SourceCardViewModel Vega { get; } = new(new AvasDevice { DeviceId = VegaMac, DeviceName = "VEGA_1100", VendorId = 192, IsTransmitter = true, IsActive = true });

            /// <summary>RX1 (4K) runs a Matrix 2x2 this tool made (TX2 in blocks 1 and 3, VEGA in 2: "3/4"); PAX355S is 4K; RX3 is 1080p and unrouted.</summary>
            public CopyRig(Func<bool>? animationsEnabled = null)
            {
                // Click handlers are async: their continuations come back to this STA thread.
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                Vm = new MainViewModel(new Configuration.ConfigService(), Client, new MockRtpReceiver(), new MulticastIpManager(),
                    new RoutingOrchestrator(Client), new MultiViewOrchestrator(Client), post: a => a(), animationsEnabled: animationsEnabled);
                Rx1 = Card(Rx1Mac, "RX1_c059e_SFPA");
                Pax = Card(PaxMac, "PAX355S_SFP A");
                Rx3 = Card(Rx3Mac, "RX3_1080P_SFPA");
                Vm.Sources.Add(Tx2);
                Vm.Sources.Add(Vega);
                Client.DisplayEdids[Rx1Mac] = TestEdids.ViewSonic4k;
                Client.DisplayEdids[PaxMac] = TestEdids.Pax355s;
                Client.DisplayEdids[Rx3Mac] = TestEdids.Build((1920, 1080, 60, false));

                Client.ReceiverStates[Rx1Mac] = new RxState(Rx1Mac, "RX1_c059e_SFPA", "MULTIVIEW", 3840, 2160, 60,
                    new[]
                    {
                        new RxSubscription(1, true, "STREAMING", Tx2Mac, "TX2_DUAL LINK_SFPA", 1),
                        new RxSubscription(2, true, "STREAMING", VegaMac, "VEGA_1100", 1),
                    },
                    new RxLayout($"avas_{Rx1Mac}_matrix2x2_3840x2160", 3840, 2160,
                        new[] { new RxWindow(0, 1), new RxWindow(1, 2), new RxWindow(2, 1) }));
                Pump(() => Vm.RestoreDisplayAsync(Rx1));
            }

            private DisplayCardViewModel Card(string mac, string name)
            {
                var d = new DisplayCardViewModel(new AvasDevice { DeviceId = mac, DeviceName = name, IsReceiver = true, IsActive = true }, Vm.UnrouteVideoAsync);
                Vm.Displays.Add(d);
                return d;
            }

            /// <summary>A display card hosted as the overview hosts it (the MainViewModel above it), at a real card size.</summary>
            public DisplayCardView View(DisplayCardViewModel d, out FrameworkElement host, double width = 420, double height = 400)
            {
                var view = new DisplayCardView { DataContext = d, Width = width, Height = height };
                host = new Border { DataContext = Vm, Child = view, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                return view;
            }
        }

        /// <summary>Pumps the dispatcher until the condition holds (async click handlers), with a deadline.</summary>
        private static void Until(Func<bool> condition, string what)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!condition() && DateTime.UtcNow < deadline) Wait(TimeSpan.FromMilliseconds(20));
            Assert.True(condition(), what);
        }

        /// <summary>
        /// A press and release on the card, the release on <paramref name="releasedOn"/>: raised as the routed mouse
        /// events (tunnel, then bubble), from which WPF derives the direct left-button events on each element of the
        /// route, as with real input.
        /// </summary>
        private static void TapCard(DisplayCardView card, UIElement? releasedOn = null)
        {
            Tap(card, Mouse.PreviewMouseDownEvent);
            Tap(releasedOn ?? card, Mouse.PreviewMouseUpEvent);
            Tap(releasedOn ?? card, Mouse.MouseUpEvent);
        }

        /// <summary>The text is shown whole: its natural size fits the space it got (nothing clipped, no ellipsis).</summary>
        private static void AssertTextFits(TextBlock label)
        {
            var probe = new TextBlock
            {
                Text = label.Text, FontSize = label.FontSize, FontWeight = label.FontWeight, FontFamily = label.FontFamily,
                FontStyle = label.FontStyle, TextWrapping = label.TextWrapping,
            };
            probe.Measure(new Size(label.TextWrapping == TextWrapping.NoWrap ? double.PositiveInfinity : label.ActualWidth, double.PositiveInfinity));
            Assert.True(probe.DesiredSize.Width <= label.ActualWidth + 0.5, $"'{label.Text}' needs {probe.DesiredSize.Width:F1} px, has {label.ActualWidth:F1}");
            Assert.True(probe.DesiredSize.Height <= label.ActualHeight + 0.5, $"'{label.Text}' needs {probe.DesiredSize.Height:F1} px high, has {label.ActualHeight:F1}");
        }

        private static void AssertInside(FrameworkElement el, FrameworkElement container)
        {
            var r = BoundsIn(el, container);
            Assert.True(r.Left >= -0.5 && r.Top >= -0.5 && r.Right <= container.ActualWidth + 0.5 && r.Bottom <= container.ActualHeight + 0.5,
                $"{el.Name} at {r} is outside {container.Name} ({container.ActualWidth:F1}x{container.ActualHeight:F1})");
        }

        // ---- Task 10b fix round 1 ----

        private static void AssertReadable(FrameworkElement el, ScrollViewer scroll, string what)
        {
            Assert.True(el.IsVisible, $"{what} is not shown");
            var r = BoundsIn(el, scroll);
            Assert.True(r.Top >= -0.5 && r.Left >= -0.5 && r.Bottom <= scroll.ViewportHeight + 0.5 && r.Right <= scroll.ViewportWidth + 0.5,
                $"{what} at {r}; the displays show {scroll.ViewportWidth:F0}x{scroll.ViewportHeight:F0}");
        }

        // ---- Task 10b fix round 2 ----

        /// <summary>The deepest element at <paramref name="point"/> (in <paramref name="from"/>'s coordinates) lies inside <paramref name="expected"/>.</summary>
        private static bool HitsInside(FrameworkElement from, Point point, DependencyObject expected)
        {
            var window = Window.GetWindow(from)!;
            var hit = window.InputHitTest(from.TranslatePoint(point, window)) as DependencyObject;
            for (var d = hit; d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (ReferenceEquals(d, expected)) return true;
            }
            return false;
        }

        private static void AssertSixteenByNine(FrameworkElement picture, string what)
        {
            double aspect = picture.ActualWidth / picture.ActualHeight;
            Assert.True(aspect >= 16.0 / 9 * 0.96 && aspect <= 16.0 / 9 * 1.04, $"{what}: picture {picture.ActualWidth:F1}x{picture.ActualHeight:F1}");
        }

        // ---- Task 11: keyboard path, automation names, reduced motion ----

        /// <summary>What the element draws now. A visual brush, so the picture does not depend on where the element lies in its parent.</summary>
        private static byte[] Pixels(FrameworkElement el)
        {
            int w = Math.Max(1, (int)Math.Ceiling(el.ActualWidth)), h = Math.Max(1, (int)Math.Ceiling(el.ActualHeight));
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) dc.DrawRectangle(new VisualBrush(el), null, new Rect(0, 0, w, h));
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var buffer = new byte[w * h * 4];
            bitmap.CopyPixels(buffer, w * 4, 0);
            return buffer;
        }

        /// <summary>
        /// Giving <paramref name="control"/> keyboard focus changes what <paramref name="paint"/> draws: a focus ring the
        /// operator can see. (Adorner rings do not render here; the controls draw theirs themselves.)
        /// </summary>
        private static void AssertShowsFocus(FrameworkElement control, FrameworkElement paint, string what)
        {
            InputModality.IsKeyboard = true; // rings are the keyboard user's
            Keyboard.ClearFocus();
            paint.UpdateLayout();
            byte[] before = Pixels(paint);
            Assert.True(control.Focus() && control.IsKeyboardFocused, $"{what} did not take keyboard focus");
            paint.UpdateLayout();
            Idle();
            byte[] after = Pixels(paint);
            Assert.False(before.SequenceEqual(after), $"{what} shows no focus ring");
        }

        /// <summary>
        /// The blocks' surfaces in block order. The visual tree lists them in paint order (the panel sorts by Z), so
        /// the list is rebuilt by API window index.
        /// </summary>
        private static List<Button> Surfaces(DependencyObject view) =>
            Named(view, "BlockSurface").Cast<Button>().OrderBy(b => ((ComposerBlockViewModel)b.DataContext).Index).ToList();

        /// <summary>The named element of the block with this API window index.</summary>
        private static FrameworkElement InBlock(DependencyObject view, string name, int index) =>
            Named(view, name).Single(e => ((ComposerBlockViewModel)e.DataContext).Index == index);

        private static T? Ancestor<T>(DependencyObject d) where T : DependencyObject
        {
            for (d = VisualTreeHelper.GetParent(d); d != null; d = VisualTreeHelper.GetParent(d))
                if (d is T found) return found;
            return null;
        }

        /// <summary>A key press on the element: the tunnelling PreviewKeyDown, then the bubbling KeyDown unless something handled the first, as real input does.</summary>
        private static void RaiseKey(UIElement target, Key key)
        {
            var preview = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Source = target,
            };
            target.RaiseEvent(preview);
            if (preview.Handled) return;
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
                Source = target,
            });
        }

        // ---- Task 11 fix round 1: the keyboard stays in the Composer, key repeat, keyboard-only rings, Undo transition ----

        /// <summary>The next key presses are the keyboard's (rings show). The input mode is process-wide state: each test sets what it needs.</summary>
        private static void KeyboardMode(bool keyboard = true) => InputModality.IsKeyboard = keyboard;

        /// <summary>A key press as the keyboard device delivers a held key: IsRepeat is only settable by the input system, so it is set on the event.</summary>
        private static void RaiseRepeatedKey(UIElement target, Key key)
        {
            KeyEventArgs Make(RoutedEvent routed)
            {
                var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key) { RoutedEvent = routed, Source = target };
                var field = typeof(KeyEventArgs).GetField("_isRepeat", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("KeyEventArgs has no _isRepeat field: " + string.Join(", ", typeof(KeyEventArgs).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Select(f => f.Name)));
                field.SetValue(args, true);
                Assert.True(args.IsRepeat);
                return args;
            }
            var preview = Make(Keyboard.PreviewKeyDownEvent);
            target.RaiseEvent(preview);
            if (!preview.Handled) target.RaiseEvent(Make(Keyboard.KeyDownEvent));
        }

        /// <summary>The keyboard focus is on an element of this Composer (its tree, or its picker popup).</summary>
        private static bool FocusIsIn(FrameworkElement view)
        {
            var popupChild = Named(view, "PickerPopup").OfType<Popup>().Single().Child;
            for (var d = Keyboard.FocusedElement as DependencyObject; d != null;
                 d = d is Visual ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            {
                if (ReferenceEquals(d, view) || ReferenceEquals(d, popupChild)) return true;
            }
            return false;
        }

        private static string FocusedName() => (Keyboard.FocusedElement as FrameworkElement)?.Name ?? Keyboard.FocusedElement?.GetType().Name ?? "(nothing)";

        /// <summary>A Composer on Matrix 2x2 with TX1 in block 1 (API index 0), shown, the keyboard on that block, its Replace / Move strip open.</summary>
        private static (ComposerRig Rig, MultiViewComposerViewModel C, MultiViewComposerView View) WithStripOpen()
        {
            var rig = new ComposerRig();
            var c = rig.Composer("matrix2x2");
            c.Arm(rig.Tx1);
            c.TapBlock(0);
            c.Disarm();
            return (rig, c, rig.View(c));
        }

        private static void OpenStripByKeyboard(MultiViewComposerView view, MultiViewComposerViewModel c, int index = 0)
        {
            KeyboardMode();
            var surface = Surfaces(view)[index];
            Assert.True(surface.Focus());
            Assert.True(view.HandleKey(Key.Enter, ModifierKeys.None, surface));
            Relayout(view);
            Assert.True(c.Blocks[index].IsSelected);
        }

        /// <summary>The colours the Dark palette gives to chrome: nothing painted with them may survive in Light mode.</summary>
        private static HashSet<Color> DarkChromeColours(params string[] keys)
        {
            var dark = new ResourceDictionary { Source = new Uri("pack://application:,,,/AvasRoutingTool;component/Resources/Themes/Theme.Dark.xaml") };
            return keys.Select(k => (Color)dark[k]).ToHashSet();
        }

        /// <summary>Every colour a laid-out view paints its chrome with (backgrounds, text, fills), as the elements resolved them.</summary>
        private static List<(string Where, Color Colour, bool IsText)> PaintedColours(DependencyObject root)
        {
            static IEnumerable<(Brush? Brush, bool IsText)> Brushes(DependencyObject d) => d switch
            {
                Border b => new[] { (b.Background, false) },
                Panel p => new[] { (p.Background, false) },
                TextBlock t => new[] { (t.Foreground, true) },
                Control c => new[] { (c.Background, false), (c.Foreground, true) },
                System.Windows.Shapes.Shape s => new[] { (s.Fill, false) },
                _ => Array.Empty<(Brush?, bool)>(),
            };
            var found = new List<(string, Color, bool)>();
            foreach (var d in AllVisuals(root))
                foreach (var (brush, isText) in Brushes(d))
                    if (brush is SolidColorBrush { Color.A: > 0 } solid)
                        found.Add(((d as FrameworkElement)?.Name is { Length: > 0 } n ? $"{d.GetType().Name} {n}" : d.GetType().Name, solid.Color, isText));
            return found;
        }

        private static IEnumerable<DependencyObject> AllVisuals(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in AllVisuals(VisualTreeHelper.GetChild(root, i)))
                    yield return child;
        }
    }
}
