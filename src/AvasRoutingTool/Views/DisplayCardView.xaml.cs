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
            if (DataContext is DisplayCardViewModel vm && vm.IsActive)
            {
                if (e.Data.GetDataPresent(typeof(AvasDevice)))
                {
                    var tx = e.Data.GetData(typeof(AvasDevice)) as AvasDevice;
                    if (tx != null && tx.IsTransmitter && tx.IsActive)
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

                if (e.Data.GetDataPresent(typeof(AvasDevice)))
                {
                    var tx = e.Data.GetData(typeof(AvasDevice)) as AvasDevice;
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
}
