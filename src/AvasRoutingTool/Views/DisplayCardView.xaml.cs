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

            // 1. A layout tile: "off" turns the display off, anything else switches its layout.
            if (TryGetLayout(e, out var layout) && layout != null)
            {
                if (string.Equals(layout.Id, "off", StringComparison.OrdinalIgnoreCase))
                {
                    if (vm.UnrouteCommand.CanExecute(null))
                    {
                        vm.UnrouteCommand.Execute(null);
                    }
                }
                else if (mainVm != null)
                {
                    await mainVm.ApplyLayoutAsync(layout.Id, vm.Device);
                }
                return;
            }

            // 2. A video source.
            if (TryGetSource(e, out var tx) && tx != null && mainVm != null)
            {
                if (vm.IsMultiView)
                {
                    // Dropped beside the windows: use the first free window, otherwise ignore.
                    if (vm.FirstEmptySlotIndex is int free)
                    {
                        await mainVm.AssignSlotAsync(vm.Device, free, tx);
                    }
                    return;
                }

                await mainVm.RouteVideoAsync(tx, vm.Device);
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

            if (Application.Current?.MainWindow?.DataContext is MainViewModel mainVm)
            {
                await mainVm.AssignSlotAsync(vm.Device, slot.Index, tx);
            }
        }
    }
}
