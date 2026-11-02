using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AvasRoutingTool.Logging;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    /// <summary>A source tile of the block picker: the source, and whether the block can take it (with the reason when not).</summary>
    public sealed class PickerTile
    {
        public PickerTile(SourceCardViewModel source, bool isEnabled, string note)
        {
            Source = source;
            IsEnabled = isEnabled;
            Note = note;
        }

        public SourceCardViewModel Source { get; }
        public bool IsEnabled { get; }
        public string Note { get; }

        /// <summary>What a screen reader says for the tile: the source, and why it is not available when it is not.</summary>
        public string AutomationName => IsEnabled ? Source.DeviceName : $"{Source.DeviceName}, not available: {Note}";
    }

    /// <summary>
    /// The MultiView Composer (spec 5.2). DataContext = <see cref="MultiViewComposerViewModel"/>. Every handler here
    /// forwards one tap, drop or drag to view-model calls; the code-behind otherwise only places the picker popup.
    /// </summary>
    public partial class MultiViewComposerView : UserControl
    {
        /// <summary>The overview's source cards: what the picker offers.</summary>
        public static readonly DependencyProperty SourcesProperty = DependencyProperty.Register(
            nameof(Sources), typeof(IEnumerable<SourceCardViewModel>), typeof(MultiViewComposerView),
            new PropertyMetadata(null, (d, _) => ((MultiViewComposerView)d).SchedulePicker()));

        /// <summary>The layout the empty Step 1 canvas draws as a ghost: the tile under the pointer or the keyboard, else the default.</summary>
        public static readonly DependencyProperty PreviewLayoutIdProperty = DependencyProperty.Register(
            nameof(PreviewLayoutId), typeof(string), typeof(MultiViewComposerView), new PropertyMetadata(DefaultPreviewLayoutId));

        private const string DefaultPreviewLayoutId = "matrix2x2";

        private MultiViewComposerViewModel? _watched;
        private bool _closingPicker;

        /// <summary>The picker was opened with the keyboard (or a key was pressed in it): the keyboard goes in, and returns to its block when it closes.</summary>
        private bool _pickerKeyboard;

        /// <summary>The block the open picker belongs to (API window index): where the keyboard returns.</summary>
        private int? _pickerBlock;

        /// <summary>Keyboard focus was inside the Composer and fell to the window (a control collapsed or went disabled): it is put back, once it can be.</summary>
        private bool _restorePending;

        /// <summary>The name of the control that had the keyboard when it fell.</summary>
        private string? _restoreFrom;

        /// <summary>A restore is queued on the dispatcher (one at a time) and how often it has been retried.</summary>
        private bool _restoreQueued;
        private int _restoreTries;

        /// <summary>
        /// The Composer was just opened by the keyboard: the keyboard goes in even though it is still on a control outside
        /// (the draft chip, the picture) that did not go away. Without this, only a keyboard that fell would be put back.
        /// </summary>
        private bool _enterPending;

        /// <summary>Tests: stands in for "the window is the active one" (the real answer cannot be changed from a test).</summary>
        internal Func<bool>? WindowActiveProbe { get; set; }

        /// <summary>The window is the foreground window. While it is not (Alt-Tab), the keyboard is not ours to move: a Focus() would activate it again.</summary>
        private bool WindowActive => WindowActiveProbe?.Invoke() ?? Window.GetWindow(this)?.IsActive == true;

        public MultiViewComposerView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => Watch(DataContext as MultiViewComposerViewModel);
            Loaded += (_, _) => SchedulePicker();
            Unloaded += (_, _) => ClosePicker();
            IsVisibleChanged += (_, _) =>
            {
                SchedulePicker();
                EnterIfOpenedByKeyboard();
            };
            PreviewKeyDown += OnPreviewKeyDown;
            LostKeyboardFocus += View_LostKeyboardFocus;
            PickerFrame.LostKeyboardFocus += View_LostKeyboardFocus;
            // A click made with the keyboard may remove or disable the control that had it (a chip, Done placing, Clear all):
            // such a control raises no LostKeyboardFocus, so the click itself asks for the check.
            AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnAnyClick), true);
        }

        public IEnumerable<SourceCardViewModel>? Sources
        {
            get => (IEnumerable<SourceCardViewModel>?)GetValue(SourcesProperty);
            set => SetValue(SourcesProperty, value);
        }

        /// <summary>The layout the ghost on the empty canvas draws (view only: nothing is staged by previewing).</summary>
        public string PreviewLayoutId
        {
            get => (string)GetValue(PreviewLayoutIdProperty);
            set => SetValue(PreviewLayoutIdProperty, value);
        }

        private MultiViewComposerViewModel? Composer => DataContext as MultiViewComposerViewModel;

        // ---- taps -> view model ----

        private async void LayoutTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ComposerLayoutOptionViewModel option } && Composer is { } c)
                await Run(() => c.SelectLayoutAsync(option.Id), "Layout selection");
        }

        // ---- hover or keyboard focus on a tile: the ghost on the empty canvas previews that layout (view only) ----

        private void LayoutTile_Preview(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ComposerLayoutOptionViewModel option }) PreviewLayoutId = option.Id;
        }

        private void LayoutTile_PreviewEnd(object sender, RoutedEventArgs e) => PreviewLayoutId = DefaultPreviewLayoutId;

        private void Block_Click(object sender, RoutedEventArgs e)
        {
            // Block commands pass the API window index (ComposerBlockViewModel.Index), never the displayed Number / Label.
            if (sender is FrameworkElement { DataContext: ComposerBlockViewModel block }) Composer?.TapBlock(block.Index);
        }

        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            // API window index (ComposerBlockViewModel.Index), never Number / Label.
            if (sender is not FrameworkElement { DataContext: ComposerBlockViewModel block } || Composer is not { } c) return;

            // Activated with the keyboard: the picker opens in keyboard mode, as the P key does (the keyboard goes in, Esc comes back).
            OpenPickerFromKeyboard(() => c.OpenPicker(block.Index), InputModality.IsKeyboard);
        }

        private void Move_Click(object sender, RoutedEventArgs e)
        {
            // API window index (ComposerBlockViewModel.Index), never Number / Label.
            if (sender is not FrameworkElement { DataContext: ComposerBlockViewModel block } || Composer is not { } c) return;
            c.BeginMove(block.Index);

            // The strip closes under the keyboard: it goes back to the block, from which an arrow key picks the destination.
            if (InputModality.IsKeyboard)
            {
                c.FocusBlock(block.Index);
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => FocusBlockSurface(block.Index)));
            }
        }

        private void PickerTile_Click(object sender, RoutedEventArgs e)
        {
            // PickerBlockIndex is the API window index the picker was opened on.
            if (sender is FrameworkElement { DataContext: PickerTile tile } && Composer is { PickerBlockIndex: int block } c)
                c.Pick(block, tile.Source);
        }

        // ---- keyboard (spec 10): the view maps keys to view-model calls and keeps the WPF focus on the block the view model names ----

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled) return;
            // Alt+key arrives as Key.System: the real key is SystemKey (menus and Alt combinations stay the window's).
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // A held Enter or Space is one press: its repeats would click a button again, or land on the first tile of the picker the first press opened.
            if (e.IsRepeat && key is Key.Enter or Key.Space)
            {
                e.Handled = true;
                return;
            }
            if (HandleKey(key, Keyboard.Modifiers, e.OriginalSource as DependencyObject, e.IsRepeat)) e.Handled = true;
        }

        /// <summary>
        /// Maps one key to the view model; true when the key was used (it is then not passed on). Arrows, Enter, Space,
        /// Delete and P act on the block the key was pressed on; Esc and Ctrl+Enter work from anywhere in the Composer.
        /// Tab is never touched, and nothing is taken while a text box is being typed in. Internal for tests (the real
        /// modifier state cannot be faked).
        /// </summary>
        internal bool HandleKey(Key key, ModifierKeys modifiers, DependencyObject? source, bool isRepeat = false)
        {
            if (Composer is not { } c || source is TextBoxBase || (modifiers & ModifierKeys.Alt) != 0) return false;

            if (key == Key.Enter && modifiers == ModifierKeys.Control)
            {
                if (isRepeat) return true; // one press, one Take
                if (c.CanTake) _ = Run(c.TakeAsync, "Take (Ctrl+Enter)");
                else
                {
                    c.ExplainTake(); // never silent: the banner says why Take is off
                    Motion.Announce(BannerText); // and says it again on every press (the text itself may not have changed)
                }
                return true;
            }
            if (key == Key.Escape && modifiers == ModifierKeys.None)
            {
                if (isRepeat) return true; // one press, one step of the ladder
                if (c.PickerBlockIndex != null) _pickerKeyboard = true; // the keyboard returns to the block
                return c.Escape();
            }
            if (modifiers != ModifierKeys.None || source is not Button { Name: "BlockSurface", DataContext: ComposerBlockViewModel block }) return false;

            // Arrows repeat (hold to walk); the keys that act once do not.
            if (isRepeat && key is Key.Enter or Key.Space or Key.P or Key.Delete or Key.Back) return true;

            // API window index, never Number / Label.
            c.FocusBlock(block.Index);
            switch (key)
            {
                case Key.Left: c.MoveFocus(FocusDirection.Left); return true;
                case Key.Right: c.MoveFocus(FocusDirection.Right); return true;
                case Key.Up: c.MoveFocus(FocusDirection.Up); return true;
                case Key.Down: c.MoveFocus(FocusDirection.Down); return true;
                case Key.Enter:
                case Key.Space:
                    OpenPickerFromKeyboard(() => c.PlaceAtFocus());
                    return true;
                case Key.P: // "pick": the source picker for this block, also on a placed one (Replace)
                    OpenPickerFromKeyboard(() => c.ArmFocused());
                    return true;
                case Key.Delete:
                case Key.Back:
                    c.ClearAtFocus();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Runs the view-model call; if it opened the picker, the keyboard goes into it.</summary>
        private void OpenPickerFromKeyboard(Action action, bool keyboard = true)
        {
            if (keyboard) _pickerKeyboard = true;
            action();
            if (Composer?.PickerBlockIndex == null) _pickerKeyboard = false;
        }

        private void BlockSurface_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // The keyboard (Tab, a click) is on this block: arrow keys start from here.
            if (sender is FrameworkElement { DataContext: ComposerBlockViewModel block }) Composer?.FocusBlock(block.Index);
        }

        /// <summary>Puts the WPF keyboard focus on a block's surface (the view model named the block).</summary>
        private bool FocusBlockSurface(int index)
        {
            var block = Composer?.Blocks.FirstOrDefault(b => b.Index == index);
            if (block == null) return false;
            return BlocksHost.ItemContainerGenerator.ContainerFromItem(block) is ContentPresenter presenter &&
                   presenter.ContentTemplate?.FindName("BlockSurface", presenter) is Button surface &&
                   surface.Focus();
        }

        // ---- the keyboard never falls out of the Composer ----

        /// <summary>The element is part of this Composer (its tree, or its picker popup).</summary>
        private bool IsInside(DependencyObject? d)
        {
            for (; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            {
                if (ReferenceEquals(d, this) || ReferenceEquals(d, PickerPopup.Child)) return true;
            }
            return false;
        }

        /// <summary>
        /// The keyboard was inside the Composer and has left it for nothing (the focused control collapsed or went disabled:
        /// Replace, Move, Done placing, Take): it is put back at the next moment it can be. A press with the mouse or a
        /// finger is never undone, and neither is focus that moved to another control.
        /// </summary>
        private void View_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!InputModality.IsKeyboard || !IsInside(e.OldFocus as DependencyObject) || IsInside(e.NewFocus as DependencyObject)) return;
            ScheduleRestore((e.OldFocus as FrameworkElement)?.Name);
        }

        private void OnAnyClick(object sender, RoutedEventArgs e)
        {
            if (InputModality.IsKeyboard) ScheduleRestore((e.OriginalSource as FrameworkElement)?.Name);
        }

        private void OnBlocksChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // The blocks were rebuilt (a layout change, a replaced draft): a block that had the keyboard is gone with no event.
            if (InputModality.IsKeyboard) ScheduleRestore(null);
        }

        /// <summary>
        /// The Composer is shown with a view model (it was opened, or opened for another display) by someone using the
        /// keyboard, and the keyboard is not inside it: it goes in, on the roving block or the first layout tile.
        /// </summary>
        private void EnterIfOpenedByKeyboard()
        {
            if (Composer == null || !IsVisible || !InputModality.IsKeyboard) return;
            if (IsInside(Keyboard.FocusedElement as DependencyObject)) return;
            ScheduleRestore(null, enter: true);
        }

        /// <summary>Asks for the keyboard to be checked at the next Input turn. Nothing is scheduled while the window is not the active one.</summary>
        private void ScheduleRestore(string? from, bool enter = false)
        {
            if (!WindowActive) return;
            if (enter) _enterPending = true;
            _restoreFrom = from;
            _restorePending = true;
            if (_restoreQueued) return;
            _restoreQueued = true;
            _restoreTries = 0;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(RestoreFocus));
        }

        /// <summary>
        /// The focused element is no longer a place for the keyboard: nothing, the window, an element that was removed from
        /// the tree, or one that is collapsed or disabled (WPF moves the focus off those a moment later, or never).
        /// </summary>
        private static bool FocusIsGone(IInputElement? focused) =>
            focused == null || focused is Window ||
            (focused is UIElement element && (!element.IsVisible || !element.IsEnabled)) ||
            (focused is DependencyObject d && PresentationSource.FromDependencyObject(d) == null);

        /// <summary>
        /// Puts the keyboard on the block that is the canvas' Tab stop; after a Take, on Undo when it is offered; failing
        /// that on Take or the first layout tile. While nothing can take it (a Take is running) it stays pending.
        /// </summary>
        private void RestoreFocus()
        {
            _restoreQueued = false;
            if (Composer is not { } c)
            {
                // The Composer was closed meanwhile: nothing is pending for whichever one is shown next.
                _restorePending = false;
                _enterPending = false;
                return;
            }
            if (!_restorePending) return;

            // Not for a pointer user, not while the window is in the background, not for a Composer that is not shown.
            // Entering: anywhere outside the Composer is wrong; otherwise only a keyboard that fell to nothing is put back.
            bool misplaced = _enterPending
                ? !IsInside(Keyboard.FocusedElement as DependencyObject)
                : FocusIsGone(Keyboard.FocusedElement);
            if (!InputModality.IsKeyboard || !WindowActive || !IsLoaded || !IsVisible || !misplaced)
            {
                _restorePending = false; // it is somewhere valid already, or somewhere else on purpose
                _enterPending = false;
                return;
            }
            if (c.PickerBlockIndex != null) // the picker opens in keyboard mode and takes it itself
            {
                _restorePending = false;
                _enterPending = false;
                return;
            }

            bool done = _restoreFrom == "TakeButton" && UndoButton.IsVisible && UndoButton.IsEnabled && UndoButton.Focus();
            if (!done)
            {
                var stop = c.Blocks.FirstOrDefault(b => b.IsTabStop);
                done = stop != null && FocusBlockSurface(stop.Index);
            }
            if (!done) done = TakeButton.IsEnabled && TakeButton.Focus();
            if (!done)
            {
                var tile = FindDescendants<Button>(LeftRailScroll).FirstOrDefault(b => b.Name == "LayoutTile" && b.IsEnabled);
                done = tile != null && tile.Focus();
            }
            _restorePending = !done;
            if (done) _enterPending = false;

            // The new blocks may not be laid out yet: try again a few times (not while a Take runs: IsBusy ends it).
            if (!done && !c.IsBusy && _restoreTries++ < 3)
            {
                _restoreQueued = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RestoreFocus));
            }
            else if (!done && !c.IsBusy)
            {
                _enterPending = false; // gave up: a later restore is only for a keyboard that fell
            }
        }

        /// <summary>The keyboard goes to the block that is the canvas' Tab stop.</summary>
        private void FocusRovingBlock()
        {
            var stop = Composer?.Blocks.FirstOrDefault(b => b.IsTabStop);
            if (stop != null) FocusBlockSurface(stop.Index);
        }

        private void OnLiveChanged(MultiViewDraft? live)
        {
            // The picture on screen changed (Take or Undo): the staged frame turns into the live one, skipped when Windows animations are off.
            if (Composer is { } c) Motion.FadeIn(CanvasFrame, c.FrameTransitionDuration, 0.35);
        }

        private void PickerFrame_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled) return;
            _pickerKeyboard = true; // a key pressed in the picker: the keyboard returns to the block when it closes
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (e.IsRepeat && key is Key.Enter or Key.Space)
            {
                e.Handled = true; // a held Enter picks once, not tile after tile
                return;
            }
            if (HandleKey(key, Keyboard.Modifiers, e.OriginalSource as DependencyObject, e.IsRepeat)) e.Handled = true;
        }

        /// <summary>The keyboard goes to the first tile that can take a source (else the first control), so Enter / arrows work at once.</summary>
        private void FocusFirstPickerTile()
        {
            if (!PickerPopup.IsOpen) return;
            var tile = FindDescendants<Button>(PickerItems).FirstOrDefault(b => b.Name == "PickerTile" && b.IsEnabled && b.IsVisible);
            if (tile != null) tile.Focus();
            else PickerFrame.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var nested in FindDescendants<T>(child)) yield return nested;
            }
        }

        private void Unplaced_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: DraftPlacement unplaced } || Composer is not { } c) return;
            c.ArmUnplaced(unplaced);

            // Tab to a source, Enter arms, an arrow key and Enter place it: the chip may be gone, the keyboard goes to the blocks.
            if (InputModality.IsKeyboard) Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusRovingBlock));
        }

        private void Disarm_Click(object sender, RoutedEventArgs e) => Composer?.Disarm();

        private void PickerCancel_Click(object sender, RoutedEventArgs e) => Composer?.Disarm();

        private async void Take_Click(object sender, RoutedEventArgs e)
        {
            if (Composer is { } c) await Run(c.TakeAsync, "Take");
        }

        private async void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (Composer is { } c) await Run(c.UndoAsync, "Undo");
        }

        private void Discard_Click(object sender, RoutedEventArgs e) => Composer?.Discard();

        private void ClearAll_Click(object sender, RoutedEventArgs e) => Composer?.ClearAll();

        /// <summary>Back to displays: the overview closes the Composer (a draft with changes is kept on the card).</summary>
        private void Close_Click(object sender, RoutedEventArgs e) => ViewLookup.FindMainViewModel(this)?.CloseComposer();

        private static async Task Run(Func<Task> action, string what)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                AppLogger.Error("COMPOSER", $"{what} failed in the view", ex);
            }
        }

        // ---- tap on an empty area: stop placing / moving (spec 6) ----

        private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (Composer is not { } c || IsOnAControl(e.OriginalSource as DependencyObject)) return;
            if (c.ArmedSource != null || c.MovingFromIndex != null || c.Blocks.Any(b => b.IsSelected)) c.Disarm();
        }

        /// <summary>A tap that landed on a block, a button, a scroll bar or the picker is theirs, not an "empty area".</summary>
        private bool IsOnAControl(DependencyObject? d)
        {
            for (; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            {
                if (ReferenceEquals(d, this)) return false;
                if (d is ButtonBase or ScrollBar or Thumb or TextBoxBase || ReferenceEquals(d, PickerPopup.Child)) return true;
            }
            return true; // outside this view's tree (another popup): not ours
        }

        // ---- drag and drop (spec 6, gesture 3): a source card onto a block places it; a block onto a block moves / swaps ----

        private readonly TapGesture _blockGesture = new();

        /// <summary>The block drag gesture (tests).</summary>
        internal TapGesture BlockGesture => _blockGesture;

        private void BlockSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _blockGesture.Press(e.GetPosition(this));

        /// <summary>Any release in the view ends the press: a later press elsewhere can never continue an old one.</summary>
        private void Root_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _blockGesture.Release();

        /// <summary>The block lost the mouse (released, window switch, drag started): forget the press.</summary>
        private void BlockSurface_LostMouseCapture(object sender, MouseEventArgs e) => _blockGesture.Release();

        private void BlockSurface_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement { DataContext: ComposerBlockViewModel block } surface) return;
            if (!block.IsPlaced || Composer is not { IsBusy: false }) return;
            if (!_blockGesture.Move(e.GetPosition(this))) return; // 8 px before a drag starts

            Mouse.Capture(null); // the block button holds the capture; the drag takes over (no Click follows)
            DragDrop.DoDragDrop(surface, new DataObject(typeof(ComposerBlockViewModel), block), DragDropEffects.Move);
            _blockGesture.Release();
        }

        private void Block_DragOver(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: ComposerBlockViewModel block } root) return;
            e.Effects = DropEffectFor(block, e.Data);
            root.Tag = e.Effects == DragDropEffects.None ? "DropNo" : "DropOk";
            e.Handled = true;
        }

        private void Block_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement root) root.Tag = null;
        }

        private void Block_Drop(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: ComposerBlockViewModel block } root) return;
            root.Tag = null;
            DropOn(block, e.Data);
            e.Handled = true;
        }

        /// <summary>What a drop on <paramref name="target"/> would do: Link (place), Move (move / swap) or None (refused, as CheckPlace says).</summary>
        internal DragDropEffects DropEffectFor(ComposerBlockViewModel target, IDataObject data)
        {
            var c = Composer;
            if (c == null || c.IsBusy || c.Draft == null) return DragDropEffects.None;

            if (data.GetDataPresent(typeof(ComposerBlockViewModel)))
            {
                var from = data.GetData(typeof(ComposerBlockViewModel)) as ComposerBlockViewModel;
                return from != null && from.IsPlaced && !ReferenceEquals(from, target) && c.Blocks.Contains(from)
                    ? DragDropEffects.Move
                    : DragDropEffects.None;
            }

            var source = SourceIn(data);
            return source != null && c.CheckPlace(target.Index, source).Ok ? DragDropEffects.Link : DragDropEffects.None;
        }

        /// <summary>
        /// Drops on a block through the view model (API window index, never Number / Label): a source is placed (refused
        /// with the reason in the banner, as a tap), a block is moved there (occupied = swap). True when the picture changed.
        /// </summary>
        internal bool DropOn(ComposerBlockViewModel target, IDataObject data)
        {
            var c = Composer;
            if (c == null || c.Draft == null) return false;
            string before = Snapshot(c);

            if (data.GetDataPresent(typeof(ComposerBlockViewModel)))
            {
                if (data.GetData(typeof(ComposerBlockViewModel)) is ComposerBlockViewModel from && !ReferenceEquals(from, target) && from.IsPlaced)
                {
                    c.BeginMove(from.Index);
                    c.TapBlock(target.Index);
                }
            }
            else if (SourceIn(data) is { } source)
            {
                c.Pick(target.Index, source);
            }
            return Snapshot(c) != before;
        }

        private SourceCardViewModel? SourceIn(IDataObject data)
        {
            string? mac = data.GetDataPresent(typeof(AvasDevice))
                ? (data.GetData(typeof(AvasDevice)) as AvasDevice)?.DeviceId
                : data.GetDataPresent(typeof(SourceCardViewModel))
                    ? (data.GetData(typeof(SourceCardViewModel)) as SourceCardViewModel)?.MacAddress
                    : null;
            if (string.IsNullOrEmpty(mac)) return null;
            string norm = SdvoeClient.NormalizeMac(mac);
            return (Sources ?? Enumerable.Empty<SourceCardViewModel>()).FirstOrDefault(s => SdvoeClient.NormalizeMac(s.MacAddress) == norm);
        }

        private static string Snapshot(MultiViewComposerViewModel c) =>
            c.Draft == null
                ? string.Empty
                : string.Join(",", c.Draft.Placements.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value.SourceMac}"));

        // ---- the picker popup: placement only ----

        /// <summary>The picker never grows past 70 % of the work area (and not below a usable 320 px); its tiles scroll.</summary>
        internal double PickerMaxHeight => Math.Max(320, Math.Round(SystemParameters.WorkArea.Height * 0.7));

        private void Watch(MultiViewComposerViewModel? composer)
        {
            if (_watched != null)
            {
                _watched.PropertyChanged -= OnComposerChanged;
                _watched.LiveChanged -= OnLiveChanged;
                _watched.Blocks.CollectionChanged -= OnBlocksChanged;
            }
            _watched = composer;
            if (_watched != null)
            {
                _watched.PropertyChanged += OnComposerChanged;
                _watched.LiveChanged += OnLiveChanged;
                _watched.Blocks.CollectionChanged += OnBlocksChanged;
            }
            SchedulePicker();
            EnterIfOpenedByKeyboard(); // shown already, now for another display's Composer
        }

        private void OnComposerChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not MultiViewComposerViewModel c) return;

            switch (e.PropertyName)
            {
                case nameof(MultiViewComposerViewModel.PickerBlockIndex):
                case nameof(MultiViewComposerViewModel.Draft):
                    SchedulePicker();
                    break;

                case nameof(MultiViewComposerViewModel.FocusedBlockIndex):
                    // The keyboard follows the view model while it is in the canvas (an arrow key moved the block).
                    if (c.FocusedBlockIndex is int focused && BlocksHost.IsKeyboardFocusWithin) FocusBlockSurface(focused);
                    break;

                case nameof(MultiViewComposerViewModel.IsBusy):
                    // A Take ended: the keyboard that fell out while it ran (Take went disabled) can come back.
                    if (!c.IsBusy && _restorePending) Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(RestoreFocus));
                    break;

                case nameof(MultiViewComposerViewModel.Banner):
                    if (c.Banner.Kind != BannerKind.None)
                    {
                        Motion.FadeIn(BannerHost, c.FadeDuration);
                        Motion.Announce(BannerText);
                    }
                    break;
            }
        }

        /// <summary>After layout, so the block's container exists to anchor to.</summary>
        private void SchedulePicker() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdatePicker));

        private void UpdatePicker()
        {
            var c = Composer;
            var block = c?.PickerBlockIndex is int index ? c.Blocks.FirstOrDefault(b => b.Index == index) : null;
            if (c == null || block == null || !IsLoaded || !IsVisible)
            {
                ClosePicker();
                return;
            }

            var tiles = (Sources ?? Enumerable.Empty<SourceCardViewModel>())
                .Select(s =>
                {
                    var check = c.CheckPlace(block.Index, s);
                    string note = check.Ok
                        ? (s.IsPreviewSupported ? "Live preview" : "No preview - the picture still goes to the screen")
                        : check.Reason ?? "This block cannot take it";
                    return new PickerTile(s, check.Ok, note);
                })
                .ToList();

            PickerTitle.Text = $"Choose a source for block {block.Number} ({block.SizeText})";
            PickerItems.ItemsSource = tiles;
            PickerEmpty.Visibility = tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PickerFrame.MaxHeight = PickerMaxHeight;
            PickerPopup.PlacementTarget = BlocksHost.ItemContainerGenerator.ContainerFromItem(block) as UIElement ?? CanvasHost;
            bool wasOpen = PickerPopup.IsOpen;
            _pickerBlock = block.Index;
            PickerPopup.IsOpen = true;

            // Opened with the keyboard: the keyboard goes into the picker, after its tiles exist.
            if (!wasOpen && _pickerKeyboard)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusFirstPickerTile));
        }

        private void ClosePicker()
        {
            if (!PickerPopup.IsOpen) return;

            // Closed from the keyboard: the keyboard returns to the block that opened the picker, never to the window.
            int? returnTo = _pickerKeyboard ? _pickerBlock : null;
            _pickerKeyboard = false;
            _pickerBlock = null;

            _closingPicker = true;
            try { PickerPopup.IsOpen = false; }
            finally { _closingPicker = false; }

            if (returnTo is int block) Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => FocusBlockSurface(block)));
        }

        /// <summary>A tap outside closed the picker: the view model closes it too.</summary>
        private void PickerPopup_Closed(object? sender, EventArgs e)
        {
            if (!_closingPicker && Composer?.PickerBlockIndex != null) Composer.Disarm();
        }
    }
}
