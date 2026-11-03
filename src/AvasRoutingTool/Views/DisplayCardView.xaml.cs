using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AvasRoutingTool.Logging;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    /// <summary>
    /// A display card of the overview (spec 5.1, 15). DataContext = <see cref="DisplayCardViewModel"/>. Every handler
    /// forwards one tap, drop or drag to one <see cref="MainViewModel"/> / card call; the code-behind otherwise only
    /// places the Layout menu, runs the copy drag (ghost, cursor, tooltip), tells a tap from a drag and scrolls a
    /// state that needs reading (drop choice, copy target, Undo toast) into view.
    /// </summary>
    public partial class DisplayCardView : UserControl
    {
        private readonly TapGesture _cardTap = new();
        private readonly TapGesture _copyGesture = new();
        private bool _tapPending;
        private ToolTip? _copyTip;
        private ToolTip? _copyReasonTip;
        private DispatcherTimer? _copyReasonTimer;
        private CopyGhostAdorner? _ghost;
        private AdornerLayer? _ghostLayer;
        private UIElement? _ghostRoot;
        private DisplayCardViewModel? _watched;

        /// <summary>When the Layout menu last closed (Environment.TickCount, the clock of input timestamps).</summary>
        private int _menuClosedTick;
        private bool _menuEverClosed;

        /// <summary>The press on the Layout button is the one that closed (or closes) the menu: its Click must not reopen it.</summary>
        private bool _pressClosesMenu;

        /// <summary>The Layout menu was opened with the keyboard (or a key was pressed in it): the keyboard goes in and comes back to the Layout button.</summary>
        private bool _menuKeyboard;

        /// <summary>One reveal per dispatcher turn: when a copy is armed every card turns into a target at once; the first (in display order) is scrolled to.</summary>
        [ThreadStatic]
        private static bool s_revealQueued;

        public DisplayCardView()
        {
            InitializeComponent();

            // The menu opens under the Layout button, right-aligned with it (above when there is no room below).
            LayoutMenuPopup.CustomPopupPlacementCallback = (popup, target, _) => new[]
            {
                new CustomPopupPlacement(new Point(target.Width - popup.Width, target.Height + 6), PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(new Point(target.Width - popup.Width, -popup.Height - 6), PopupPrimaryAxis.Horizontal),
            };
            DataContextChanged += (_, _) => Watch(IsLoaded ? DataContext as DisplayCardViewModel : null);
            Loaded += (_, _) => Watch(DataContext as DisplayCardViewModel);
            Unloaded += (_, _) =>
            {
                Watch(null);
                HideCopyTip();
                HideCopyReason();
                HideGhost();
            };
        }

        /// <summary>Starts the OLE drag of a copy; tests replace it (a real DoDragDrop runs a modal loop).</summary>
        internal Func<DependencyObject, IDataObject, DragDropEffects, DragDropEffects> DragStarter { get; set; } =
            (source, data, allowed) => DragDrop.DoDragDrop(source, data, allowed);

        /// <summary>The pointer is still pressed after the arm's EDID reads (else no drag starts); tests replace it.</summary>
        internal Func<bool> IsPointerDown { get; set; } = () => Mouse.LeftButton == MouseButtonState.Pressed;

        private DisplayCardViewModel? Card => DataContext as DisplayCardViewModel;

        private MainViewModel? Main => ViewLookup.FindMainViewModel(this);

        private static async Task Run(Func<Task> action, string what)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                AppLogger.Error("UI", $"{what} failed on a display card", ex);
            }
        }

        // ---- a state that must be read is scrolled into view (spec 15: the target says it in words) ----

        private void Watch(DisplayCardViewModel? card)
        {
            if (ReferenceEquals(card, _watched)) return;
            if (_watched != null) _watched.PropertyChanged -= OnCardChanged;
            _watched = card;
            if (_watched != null) _watched.PropertyChanged += OnCardChanged;
            HideCopyReason(); // a reason belongs to the display it was asked about
        }

        private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not DisplayCardViewModel vm) return;

            // The menu closed (StaysOpen=False pushes it through the TwoWay binding at once; Popup.Closed comes later).
            if (e.PropertyName == nameof(DisplayCardViewModel.IsLayoutMenuOpen) && !vm.IsLayoutMenuOpen)
            {
                RecordMenuClosed();
                return;
            }

            // The display can be copied now: the reason it said why not is no longer true.
            if (e.PropertyName == nameof(DisplayCardViewModel.CanCopyFrom) && vm.CanCopyFrom)
            {
                HideCopyReason();
                return;
            }

            // A change a screen reader should hear, and the toast fading in (skipped when Windows animations are off).
            if (e.PropertyName == nameof(DisplayCardViewModel.CanUndoScreen) && vm.CanUndoScreen)
                Motion.FadeIn(UndoToast, Main?.FadeDuration ?? TimeSpan.Zero);
            else if (e.PropertyName == nameof(DisplayCardViewModel.UndoToastText) && vm.UndoToastText != null)
                Motion.Announce(UndoToastLabel);
            else if (e.PropertyName == nameof(DisplayCardViewModel.LastError) && vm.HasError)
                Motion.Announce(ErrorBannerText);

            bool turnedOn = e.PropertyName switch
            {
                nameof(DisplayCardViewModel.IsDropChoiceOpen) => vm.IsDropChoiceOpen,
                nameof(DisplayCardViewModel.IsCopyTargetAllowed) => vm.IsCopyTargetAllowed,
                nameof(DisplayCardViewModel.IsCopyTargetBlocked) => vm.IsCopyTargetBlocked,
                nameof(DisplayCardViewModel.CanUndoScreen) => vm.CanUndoScreen,
                _ => false,
            };
            if (!turnedOn || s_revealQueued) return;
            s_revealQueued = true;
            // After layout (the overview gives the displays more room as the state turns on).
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                s_revealQueued = false;
                RevealState();
            }));
        }

        /// <summary>
        /// Scrolls the card's text-bearing state into the displays' view: the drop choice, the copy target's words,
        /// the Undo toast (together when shown together).
        /// </summary>
        internal void RevealState()
        {
            if (!IsLoaded) return;
            Rect? area = null;
            foreach (FrameworkElement part in new FrameworkElement[] { DropChoiceContent, CopyTargetAllowedContent, CopyTargetBlockedContent, UndoToast })
            {
                if (!part.IsVisible) continue;
                var r = part.TransformToAncestor(this).TransformBounds(new Rect(part.RenderSize));
                area = area is { } a ? Rect.Union(a, r) : r;
            }
            if (area is { } show) BringIntoView(show);
        }

        // ---- tap on the card: MainViewModel.TapDisplayAsync / TapPictureAsync ----

        private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _tapPending = false;
            _cardTap.Press(e.GetPosition(this));
        }

        /// <summary>Movement past the 8 px threshold makes the press a drag (e.g. a scroll), never a tap.</summary>
        private void Card_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) _cardTap.Move(e.GetPosition(this));
        }

        /// <summary>
        /// Every release over the card ends its press here (it tunnels through the card even when a button handles the
        /// release), so a stale press can never turn a later release into a tap. A release on one of the card's buttons
        /// is the button's (it handles the bubbling release, so this is where it is decided).
        /// </summary>
        private void Card_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
            _tapPending = _cardTap.Release() && !IsOnAControl(e.OriginalSource as DependencyObject);

        /// <summary>A tap on the picture may open the Composer (MultiView, nothing armed); anywhere else it is a card tap.</summary>
        private async void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            bool tap = _tapPending;
            _tapPending = false;
            var on = e.OriginalSource as DependencyObject;
            if (!tap || IsOnAControl(on) || Card is not { } vm || Main is not { } main) return;
            if (IsInside(on, PreviewSurface))
                await Run(() => main.TapPictureAsync(vm), "Tap on the picture");
            else
                await Run(() => main.TapDisplayAsync(vm), "Tap");
        }

        /// <summary>
        /// Enter or Space on the focused picture: as a tap on it - opens the Composer on a MultiView card, or, while a
        /// copy is armed, copies onto this display (a blocked target does nothing; its name says why).
        /// </summary>
        private async void PreviewSurface_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is not (Key.Enter or Key.Space) || Card is not { } vm || Main is not { } main) return;
            e.Handled = true;
            if (e.IsRepeat) return; // a held key is one tap
            await Run(() => main.TapPictureAsync(vm), "Enter on the picture");
        }

        /// <summary>
        /// A held Enter or Space is one press: its repeats would arm and disarm the Copy layout handle, reopen the Layout
        /// menu or tap the picture again. (Tunnelling, so the handle's own click never sees them.)
        /// </summary>
        private void Card_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.IsRepeat && e.Key is Key.Enter or Key.Space) e.Handled = true;
        }

        /// <summary>Esc anywhere on the card stops what is armed (a copy first), as the arm banner's Stop does.</summary>
        private void Card_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled || e.Key != Key.Escape || Main is not { } main) return;
            if (main.Escape()) e.Handled = true;
        }

        private static DependencyObject? ParentOf(DependencyObject d) =>
            d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

        private static bool IsInside(DependencyObject? d, DependencyObject container)
        {
            for (; d != null; d = ParentOf(d))
            {
                if (ReferenceEquals(d, container)) return true;
            }
            return false;
        }

        /// <summary>A tap on one of the card's buttons (Layout, Copy layout, Turn Off, a choice, Undo) is the button's.</summary>
        private bool IsOnAControl(DependencyObject? d)
        {
            for (; d != null; d = ParentOf(d))
            {
                if (ReferenceEquals(d, this)) return false;
                if (d is ButtonBase or ScrollBar or Thumb) return true;
            }
            return true; // outside this card's tree (a popup): not a tap on the card
        }

        // ---- the header's Layout menu ----

        /// <summary>
        /// The press decides, not the time to the release (a slow gloved press): it closes the menu when the menu is open
        /// now, or when the menu closed at or after this press went down (the popup handled the press first).
        /// </summary>
        private void LayoutButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pressClosesMenu = LayoutMenuPopup.IsOpen || (_menuEverClosed && unchecked(e.Timestamp - _menuClosedTick) <= 0);
        }

        /// <summary>The flag lives for one press: it is dropped after this release and its Click (the button holds the capture, so the release comes here).</summary>
        private void LayoutButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => _pressClosesMenu = false));

        /// <summary>Enter or Space on the Layout button opens the menu with the keyboard: the keyboard then goes into the menu.</summary>
        private void LayoutButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Enter or Key.Space) _menuKeyboard = true;
        }

        private void LayoutMenu_Opened(object? sender, EventArgs e)
        {
            if (!_menuKeyboard) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (!LayoutMenuPopup.IsOpen) return;
                var tile = FindDescendants<Button>(LayoutMenuItems).FirstOrDefault(b => b.Name == "LayoutMenuTile" && b.IsEnabled && b.IsVisible);
                if (tile != null) tile.Focus();
                else LayoutMenuFrame.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }));
        }

        /// <summary>Esc closes the menu and the keyboard goes back to the Layout button; any other key marks the menu as keyboard-driven.</summary>
        private void LayoutMenuFrame_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            _menuKeyboard = true;
            if (e.IsRepeat && e.Key is Key.Enter or Key.Space) e.Handled = true; // a held Enter chooses one tile
            if (e.Key != Key.Escape || Card is not { } vm) return;
            e.Handled = true;
            vm.IsLayoutMenuOpen = false;
            ReturnToLayoutButton();
        }

        private void ReturnToLayoutButton()
        {
            if (!_menuKeyboard) return;
            _menuKeyboard = false;
            // The choice opened the Composer, which has taken the displays' place (and the keyboard): this button is gone.
            if (Main?.ActiveComposer != null) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (Main?.ActiveComposer == null) LayoutButton.Focus();
            }));
        }

        private static System.Collections.Generic.IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var nested in FindDescendants<T>(child)) yield return nested;
            }
        }

        private void LayoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (Card is not { } vm) return;
            if (_pressClosesMenu)
            {
                _pressClosesMenu = false; // this press closed the menu: it does not open it again
                return;
            }
            vm.IsLayoutMenuOpen = true;
        }

        /// <summary>Fallback only: the close is normally recorded when IsLayoutMenuOpen turns false (Closed is raised later).</summary>
        private void LayoutMenu_Closed(object? sender, EventArgs e)
        {
            if (_watched == null) RecordMenuClosed();
        }

        private void RecordMenuClosed()
        {
            _menuClosedTick = Environment.TickCount;
            _menuEverClosed = true;
        }

        /// <summary>When the menu last closed (tests).</summary>
        internal int MenuClosedTick => _menuClosedTick;

        /// <summary>One layout choice for the popup tiles and the inline presets (the DataContext is the layout).</summary>
        private async void LayoutChoice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: LayoutItemViewModel item } || Card is not { } vm) return;
            if (Main is { } main)
                await Run(() => main.ChooseLayoutFromCardAsync(vm, item.Id), "Layout choice"); // closes the menu
            else
                vm.IsLayoutMenuOpen = false;
            ReturnToLayoutButton();
        }

        // ---- the drop choice, the draft chip, the Undo toast ----

        private async void FullScreen_Click(object sender, RoutedEventArgs e)
        {
            if (Card is { } vm && Main is { } main) await Run(() => main.ChooseFullScreenAsync(vm), "Full screen");
        }

        private async void IntoBlock_Click(object sender, RoutedEventArgs e)
        {
            if (Card is { } vm && Main is { } main) await Run(() => main.ChooseIntoBlockAsync(vm), "Into a block");
        }

        private async void DraftChip_Click(object sender, RoutedEventArgs e)
        {
            if (Card is { } vm && Main is { } main) await Run(() => main.OpenComposerAsync(vm), "Resume draft");
        }

        private async void UndoScreen_Click(object sender, RoutedEventArgs e)
        {
            if (Card is { } vm && Main is { } main) await Run(() => main.UndoScreenAsync(vm), "Undo");
        }

        // ---- Copy layout: a tap arms (tap again to stop); a drag from this handle only (8 px) copies on release ----

        private async void CopyHandle_Click(object sender, RoutedEventArgs e)
        {
            if (Card is not { } vm) return;
            if (!vm.CanCopyFrom)
            {
                // Nothing to copy: the handle takes the press but does not arm; the reason is said next to it for a while.
                if (sender is UIElement anchor) ShowCopyReason(vm.CopyFromBlockedText, anchor);
                return;
            }
            if (Main is { } main) await Run(() => main.ToggleCopyArmAsync(vm), "Copy layout");
        }

        /// <summary>The reason shown by the last press on a Copy layout handle with nothing to copy (tests); null before the first one.</summary>
        internal ToolTip? CopyReasonTip => _copyReasonTip;

        /// <summary>How long the reason for a blocked Copy layout press stays open (tests shorten it).</summary>
        internal TimeSpan CopyReasonShownFor { get; set; } = TimeSpan.FromSeconds(3);

        private void ShowCopyReason(string text, UIElement anchor)
        {
            HideCopyReason();
            _copyReasonTip = new ToolTip
            {
                PlacementTarget = anchor,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                MaxWidth = 360,
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13 },
            };
            _copyReasonTip.IsOpen = true;
            _copyReasonTimer = new DispatcherTimer { Interval = CopyReasonShownFor };
            _copyReasonTimer.Tick += (_, _) => HideCopyReason();
            _copyReasonTimer.Start();
        }

        private void HideCopyReason()
        {
            _copyReasonTimer?.Stop();
            _copyReasonTimer = null;
            if (_copyReasonTip != null) _copyReasonTip.IsOpen = false;
        }

        private void CopyHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _copyGesture.Press(e.GetPosition(this));

        private void CopyHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _copyGesture.Release();

        private async void CopyHandle_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || sender is not UIElement handle) return;
            if (!_copyGesture.Move(e.GetPosition(this))) return; // 8 px before a drag starts; a tap stays a tap
            await StartCopyDragAsync(handle);
        }

        /// <summary>
        /// The copy drag: arms this display (the other cards show allowed / blocked; reads their EDIDs), then drags a
        /// data object naming this card with a ghost of its picture; always disarms afterwards (spec 15).
        /// </summary>
        internal async Task StartCopyDragAsync(UIElement handle)
        {
            if (Card is not { CanCopyFrom: true } vm || Main is not { } main) return;
            if (Mouse.Captured != null) Mouse.Capture(null); // the button holds the capture; the drag takes over (no Click follows)
            try
            {
                await main.ArmDisplayAsync(vm);
                if (!ReferenceEquals(main.ArmedDisplay, vm) || !IsPointerDown()) return; // nothing to copy, or released meanwhile

                ShowGhost(vm);
                handle.GiveFeedback += Handle_GiveFeedback;
                try
                {
                    DragStarter(handle, new DataObject(typeof(DisplayCardViewModel), vm), DragDropEffects.Copy);
                }
                finally
                {
                    handle.GiveFeedback -= Handle_GiveFeedback;
                    HideGhost();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("COPY", "Copy drag failed on a display card", ex);
            }
            finally
            {
                main.DisarmDisplay();
                _copyGesture.Release();
            }
        }

        private void Handle_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            e.UseDefaultCursors = true; // Copy on an allowed target, no-drop on a blocked one (the targets' DragOver)
            if (_ghostRoot != null && PresentationSource.FromVisual(_ghostRoot) != null && GetCursorPos(out var cursor))
                PlaceGhostAt(_ghostRoot.PointFromScreen(new Point(cursor.X, cursor.Y)));
        }

        // ---- the drag ghost: an adorner in the window's adorner layer, never hit-testable, removed when the drag ends ----

        /// <summary>The ghost while a copy is dragged (tests).</summary>
        internal CopyGhostAdorner? Ghost => _ghost;

        /// <summary>The element the ghost's coordinates refer to: the window's content (tests).</summary>
        internal UIElement? GhostRoot => _ghostRoot;

        /// <summary>Places the ghost beside the pointer, given in <see cref="GhostRoot"/> coordinates.</summary>
        internal void PlaceGhostAt(Point pointer) => _ghost?.MoveTo(pointer);

        private void ShowGhost(DisplayCardViewModel vm)
        {
            HideGhost();
            // The window's own layer (not the displays ScrollViewer's), so the ghost can follow the pointer anywhere in it.
            _ghostRoot = Window.GetWindow(this)?.Content as UIElement ?? this;
            _ghostLayer = AdornerLayer.GetAdornerLayer(_ghostRoot) ?? AdornerLayer.GetAdornerLayer(this);
            if (_ghostLayer == null) return;

            var picture = new Rectangle
            {
                Height = 90,
                RadiusX = 4,
                RadiusY = 4,
                Fill = new VisualBrush(PreviewSurface) { Stretch = Stretch.Uniform },
            };
            var title = new TextBlock
            {
                Text = $"Copying {vm.DeviceName}",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("Brush.Text.CopyAccent"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4, 0, 0),
            };
            var badge = new TextBlock
            {
                Text = vm.ModeBadge,
                FontSize = 11,
                Foreground = (Brush)FindResource("Brush.Text.OnCopyTarget"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var frame = new Border
            {
                Width = 184,
                Background = (Brush)FindResource("Brush.Surface.Popup"),
                BorderBrush = (Brush)FindResource("Brush.Border.DropTarget"),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6),
                Child = new StackPanel { Children = { picture, title, badge } },
            };

            _ghost = new CopyGhostAdorner(_ghostRoot, frame);
            _ghostLayer.Add(_ghost);
            if (GetCursorPos(out var cursor) && PresentationSource.FromVisual(_ghostRoot) != null)
                PlaceGhostAt(_ghostRoot.PointFromScreen(new Point(cursor.X, cursor.Y)));
        }

        private void HideGhost()
        {
            if (_ghost != null)
            {
                _ghostLayer?.Remove(_ghost);
                _ghost.Detach();
            }
            _ghost = null;
            _ghostLayer = null;
            _ghostRoot = null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorPoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out CursorPoint point);

        // ---- drops on this card: a copy (spec 15), a layout tile, a source ----

        private static bool TryGetSource(IDataObject data, out AvasDevice? tx)
        {
            tx = null;
            if (data.GetDataPresent(typeof(AvasDevice)))
            {
                tx = data.GetData(typeof(AvasDevice)) as AvasDevice;
            }
            else if (data.GetDataPresent(typeof(SourceCardViewModel)))
            {
                tx = (data.GetData(typeof(SourceCardViewModel)) as SourceCardViewModel)?.Device;
            }
            return tx != null && tx.IsTransmitter;
        }

        private static bool TryGetLayout(IDataObject data, out LayoutItemViewModel? layout)
        {
            layout = data.GetDataPresent(typeof(LayoutItemViewModel))
                ? data.GetData(typeof(LayoutItemViewModel)) as LayoutItemViewModel
                : null;
            return layout != null && layout.IsAvailable;
        }

        private static DisplayCardViewModel? CopySourceIn(IDataObject data) =>
            data.GetDataPresent(typeof(DisplayCardViewModel)) ? data.GetData(typeof(DisplayCardViewModel)) as DisplayCardViewModel : null;

        private void Card_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragOverCard(e.Data);
            e.Handled = true;
        }

        /// <summary>
        /// What a drop here would do. A copy: only <see cref="CopyDragFeedback"/> with the overview's decision for this
        /// card (copy / no-drop cursor and the tooltip); sources and layout tiles: Link on an available display.
        /// </summary>
        internal DragDropEffects DragOverCard(IDataObject data)
        {
            if (Card is not { } vm) return DragDropEffects.None;

            if (CopySourceIn(data) != null)
            {
                if (Main is not { } main) return DragDropEffects.None;
                var decision = main.CopyDecisionFor(vm);
                ShowCopyTip(CopyDragFeedback.Tooltip(decision));
                return CopyDragFeedback.Effects(decision);
            }

            // Sources and layout tiles (Off, Single View, MultiView presets) are all valid payloads.
            if (vm.IsActive && (TryGetSource(data, out _) || TryGetLayout(data, out _)))
            {
                vm.IsDragOver = true;
                return DragDropEffects.Link;
            }
            return DragDropEffects.None;
        }

        private void Card_DragLeave(object sender, DragEventArgs e) => DragLeaveCard();

        internal void DragLeaveCard()
        {
            if (Card is { } vm) vm.IsDragOver = false;
            HideCopyTip();
        }

        private async void Card_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            var data = e.Data;
            await Run(() => DropOnCardAsync(data), "Drop");
        }

        /// <summary>A drop on the card through the overview: copy here, a layout tile, or a source. True when it was acted on.</summary>
        internal async Task<bool> DropOnCardAsync(IDataObject data)
        {
            HideCopyTip();
            if (Card is not { } vm) return false;
            vm.IsDragOver = false;
            var main = Main;

            // 1. A copy: what the dragged display shows, onto this one (a blocked copy sends nothing).
            if (CopySourceIn(data) is { } source)
                return main != null && await main.CopyDisplayAsync(source, vm);

            // 2. A layout tile: as the card's Layout menu (a preset opens the Composer staged; Single View / Off as today).
            if (TryGetLayout(data, out var layout) && layout != null)
            {
                if (main != null) return await main.ChooseLayoutFromCardAsync(vm, layout.Id);
                if (string.Equals(layout.Id, "off", StringComparison.OrdinalIgnoreCase) && vm.UnrouteCommand.CanExecute(null))
                {
                    vm.UnrouteCommand.Execute(null);
                    return true;
                }
                return false;
            }

            // 3. A video source: a MultiView card asks (drop choice), any other card routes it in Single View.
            if (TryGetSource(data, out var tx) && tx != null && main != null)
                return await main.DropSourceAsync(vm, tx);
            return false;
        }

        // ---- the copy tooltip for mouse users (the card's own text stays the primary explanation) ----

        /// <summary>The reason / note the copy tooltip shows now; null when it is closed (tests).</summary>
        internal string? CopyTipText => _copyTip is { IsOpen: true, Content: TextBlock text } ? text.Text : null;

        private void ShowCopyTip(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                HideCopyTip();
                return;
            }
            // Below the card: never under the pointer, where it would take the drop.
            _copyTip ??= new ToolTip { Placement = PlacementMode.Bottom, PlacementTarget = CardFrame, MaxWidth = 360 };
            if (_copyTip.Content is not TextBlock block)
            {
                block = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
                _copyTip.Content = block;
            }
            block.Text = text;
            _copyTip.IsOpen = true;
        }

        private void HideCopyTip()
        {
            if (_copyTip != null) _copyTip.IsOpen = false;
        }

        // --- Multiview window drop zones -------------------------------------------------------

        private void Slot_DragOver(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: WindowSlotViewModel slot } &&
                DataContext is DisplayCardViewModel vm && vm.IsActive && TryGetSource(e.Data, out _))
            {
                slot.IsDragOver = true;
                e.Effects = DragDropEffects.Link;
                e.Handled = true;
            }
            // Anything else (a copy, the Off tile) keeps bubbling to the card.
        }

        private void Slot_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: WindowSlotViewModel slot })
            {
                slot.IsDragOver = false;
            }
        }

        private async void Slot_Drop(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: WindowSlotViewModel slot } ||
                DataContext is not DisplayCardViewModel vm) return;

            slot.IsDragOver = false;
            if (!TryGetSource(e.Data, out var tx) || tx == null) return; // a copy or a layout tile: the card handles it

            e.Handled = true;
            vm.IsDragOver = false;

            // A block of the live mini view is not a place to edit: the drop choice decides (Into a block... opens the Composer).
            if (Main is { } mainVm) await Run(() => mainVm.DropSourceAsync(vm, tx), "Drop on a block");
        }
    }
}
