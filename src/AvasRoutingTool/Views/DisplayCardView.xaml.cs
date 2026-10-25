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
                if (e.Data.GetDataPresent(typeof(AvasDevice)) || e.Data.GetDataPresent(typeof(SourceCardViewModel)))
                {
                    vm.IsDragOver = true;
                    e.Effects = DragDropEffects.Link;
                    e.Handled = true;
                    return;
                }
                else if (e.Data.GetDataPresent(typeof(LayoutItemViewModel)))
                {
                    var layout = e.Data.GetData(typeof(LayoutItemViewModel)) as LayoutItemViewModel;
                    if (layout != null && string.Equals(layout.Id, "off", StringComparison.OrdinalIgnoreCase))
                    {
                        vm.IsDragOver = true;
                        e.Effects = DragDropEffects.Link;
                        e.Handled = true;
                        return;
                    }
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
            if (DataContext is DisplayCardViewModel vm)
            {
                vm.IsDragOver = false;

                // 1. Check if dropped layout item is "off"
                if (e.Data.GetDataPresent(typeof(LayoutItemViewModel)))
                {
                    var layout = e.Data.GetData(typeof(LayoutItemViewModel)) as LayoutItemViewModel;
                    if (layout != null && string.Equals(layout.Id, "off", StringComparison.OrdinalIgnoreCase))
                    {
                        if (vm.UnrouteCommand.CanExecute(null))
                        {
                            vm.UnrouteCommand.Execute(null);
                        }
                        return;
                    }
                }

                // 2. Check if dropped item is a video Source
                AvasDevice? tx = null;
                if (e.Data.GetDataPresent(typeof(AvasDevice)))
                {
                    tx = e.Data.GetData(typeof(AvasDevice)) as AvasDevice;
                }
                else if (e.Data.GetDataPresent(typeof(SourceCardViewModel)))
                {
                    var sourceVm = e.Data.GetData(typeof(SourceCardViewModel)) as SourceCardViewModel;
                    tx = sourceVm?.Device;
                }

                if (tx != null && tx.IsTransmitter)
                {
                    if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm)
                    {
                        await mainVm.RouteVideoAsync(tx, vm.Device);
                    }
                }
            }
        }
    }
}
