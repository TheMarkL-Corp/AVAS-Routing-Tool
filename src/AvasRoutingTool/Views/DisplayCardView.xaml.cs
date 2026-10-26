using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AvasRoutingTool.Logging;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    /// <summary>
    /// A display card of the overview (spec 5.1, 15). DataContext = <see cref="DisplayCardViewModel"/>. Every handler
    /// forwards one tap, drop or drag to one <see cref="MainViewModel"/> / card call; the code-behind otherwise only
    /// places the Layout menu, runs the copy drag (ghost, cursor, tooltip) and tells a tap from a drag.
    /// </summary>
    public partial class DisplayCardView : UserControl
    {
        /// <summary>A Layout button tap that also closed the menu (StaysOpen=False) must not open it again.</summary>
        private const long MenuReopenGuardMs = 300;

        private readonly TapGesture _cardTap = new();
        private readonly TapGesture _copyGesture = new();
        private long _menuClosedAt = long.MinValue / 2;
        private bool _tapPending;
        private ToolTip? _copyTip;

        public DisplayCardView()
        {
            InitializeComponent();

            // The menu opens under the Layout button, right-aligned with it (above when there is no room below).
            LayoutMenuPopup.CustomPopupPlacementCallback = (popup, target, _) => new[]
            {
                new CustomPopupPlacement(new Point(target.Width - popup.Width, target.Height + 6), PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(new Point(target.Width - popup.Width, -popup.Height - 6), PopupPrimaryAxis.Horizontal),
            };
            Unloaded += (_, _) =>
            {
                HideCopyTip();
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

        // ---- tap on the card: MainViewModel.TapDisplayAsync (route an armed source, copy an armed display) ----

        private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _cardTap.Press(e.GetPosition(this));

        /// <summary>Movement past the 8 px threshold makes the press a drag (e.g. a scroll), never a tap.</summary>
        private void Card_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) _cardTap.Move(e.GetPosition(this));
        }

        /// <summary>
        /// Every release over the card ends its press here (it tunnels through the card even when a button handles the
        /// release), so a stale press can never turn a later release into a tap.
        /// </summary>
        private void Card_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _tapPending = _cardTap.Release();

        private async void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            bool tap = _tapPending;
            _tapPending = false;
            if (!tap || IsOnAControl(e.OriginalSource as DependencyObject)) return;
            if (Card is { } vm && Main is { } main) await Run(() => main.TapDisplayAsync(vm), "Tap");
        }

        /// <summary>A tap on one of the card's buttons (Layout, Copy layout, Turn Off, a choice, Undo) is the button's.</summary>
        private bool IsOnAControl(DependencyObject? d)
        {
            for (; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            {
                if (ReferenceEquals(d, this)) return false;
                if (d is ButtonBase or ScrollBar or Thumb) return true;
            }
            return true; // outside this card's tree (a popup): not a tap on the card
        }

        // ---- the header's Layout menu ----

        private void LayoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (Card is not { } vm) return;
            if (Environment.TickCount64 - _menuClosedAt < MenuReopenGuardMs) return; // this tap just closed it
            vm.IsLayoutMenuOpen = true;
        }

        private void LayoutMenu_Closed(object? sender, EventArgs e) => _menuClosedAt = Environment.TickCount64;

        private async void LayoutMenuTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: LayoutItemViewModel item } || Card is not { } vm) return;
            if (Main is { } main)
                await Run(() => main.ChooseLayoutFromCardAsync(vm, item.Id), "Layout choice"); // closes the menu
            else
                vm.IsLayoutMenuOpen = false;
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

        private void UndoScreen_Click(object sender, RoutedEventArgs e) => Card?.UndoScreenCommand.Execute(null);

        // ---- Copy layout: a tap arms (tap again to stop); a drag from this handle only (8 px) copies on release ----

        private void CopyHandle_Click(object sender, RoutedEventArgs e)
        {
            if (Card is { } vm && vm.CopyLayoutCommand.CanExecute(null)) vm.CopyLayoutCommand.Execute(null);
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

                ShowGhost();
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
            MoveGhost();
        }

        // ---- the drag ghost (a VisualBrush of this card's picture; released when the drag ends) ----

        private void ShowGhost()
        {
            GhostPicture.Fill = new VisualBrush(PreviewSurface) { Stretch = Stretch.Uniform };
            MoveGhost();
            CopyGhost.IsOpen = true;
        }

        private void HideGhost()
        {
            CopyGhost.IsOpen = false;
            GhostPicture.Fill = null;
        }

        private void MoveGhost()
        {
            if (PresentationSource.FromVisual(CardFrame) == null || !GetCursorPos(out var cursor)) return;
            var at = CardFrame.PointFromScreen(new Point(cursor.X, cursor.Y));
            // Beside the pointer, never under it: the window under the pointer is the drop target.
            CopyGhost.HorizontalOffset = at.X + 18;
            CopyGhost.VerticalOffset = at.Y + 18;
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
