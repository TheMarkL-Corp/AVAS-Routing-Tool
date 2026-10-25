using System;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    public partial class DisplayCardView : UserControl
    {
        public DisplayCardView()
        {
            InitializeComponent();
        }

        private static bool TryGetSource(DragEventArgs e, out AvasDevice? tx)
        {
            tx = null;
            if (e.Data.GetDataPresent(typeof(AvasDevice)))
            {
                tx = e.Data.GetData(typeof(AvasDevice)) as AvasDevice;
            }
            else if (e.Data.GetDataPresent(typeof(SourceCardViewModel)))
            {
                tx = (e.Data.GetData(typeof(SourceCardViewModel)) as SourceCardViewModel)?.Device;
            }
            return tx != null && tx.IsTransmitter;
        }

        private static bool TryGetLayout(DragEventArgs e, out LayoutItemViewModel? layout)
        {
            layout = e.Data.GetDataPresent(typeof(LayoutItemViewModel))
                ? e.Data.GetData(typeof(LayoutItemViewModel)) as LayoutItemViewModel
                : null;
            return layout != null && layout.IsAvailable;
        }

        private void Card_DragEnter(object sender, DragEventArgs e)
        {
            HandleDragVerification(e);
        }

        private void Card_DragOver(object sender, DragEventArgs e)
        {
            HandleDragVerification(e);
        }

        private void HandleDragVerification(DragEventArgs e)
        {
            if (DataContext is DisplayCardViewModel vm && vm.IsActive)
            {
                // Sources and layout tiles (Off, Single View, MultiView presets) are all valid payloads.
                if (e.Data.GetDataPresent(typeof(AvasDevice)) || e.Data.GetDataPresent(typeof(SourceCardViewModel)) ||
                    TryGetLayout(e, out _))
                {
                    vm.IsDragOver = true;
                    e.Effects = DragDropEffects.Link;
                    e.Handled = true;
                    return;
                }
            }
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Card_DragLeave(object sender, DragEventArgs e)
        {
            if (DataContext is DisplayCardViewModel vm)
            {
                vm.IsDragOver = false;
            }
        }

        private async void Card_Drop(object sender, DragEventArgs e)
        {
            if (DataContext is not DisplayCardViewModel vm) return;

            vm.IsDragOver = false;
            var mainVm = Application.Current?.MainWindow?.DataContext as MainViewModel;

            // 1. A layout tile: as the card's Layout menu (a preset opens the Composer staged; Single View / Off as today).
            if (TryGetLayout(e, out var layout) && layout != null)
            {
                if (mainVm != null)
                {
                    await mainVm.ChooseLayoutFromCardAsync(vm, layout.Id);
                }
                else if (string.Equals(layout.Id, "off", StringComparison.OrdinalIgnoreCase) && vm.UnrouteCommand.CanExecute(null))
                {
                    vm.UnrouteCommand.Execute(null);
                }
                return;
            }

            // 2. A video source: a MultiView card asks (drop choice), any other card routes it in Single View.
            if (TryGetSource(e, out var tx) && tx != null && mainVm != null)
            {
                await mainVm.DropSourceAsync(vm, tx);
            }
        }

        // --- Multiview window drop zones -------------------------------------------------------

        private void Slot_DragOver(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: WindowSlotViewModel slot } &&
                DataContext is DisplayCardViewModel vm && vm.IsActive && TryGetSource(e, out _))
            {
                slot.IsDragOver = true;
                e.Effects = DragDropEffects.Link;
                e.Handled = true;
            }
            // Anything else (e.g. the Off tile) keeps bubbling to the card.
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
            if (!TryGetSource(e, out var tx) || tx == null) return;

            e.Handled = true;
            vm.IsDragOver = false;

            // A block of the live mini view is not a place to edit: the drop choice decides (Into a block... opens the Composer).
            if (Application.Current?.MainWindow?.DataContext is MainViewModel mainVm)
            {
                await mainVm.DropSourceAsync(vm, tx);
            }
        }
    }
}
