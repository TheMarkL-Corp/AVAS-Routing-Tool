using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    public partial class SourceCardView : UserControl
    {
        private Point _dragStartPoint;

        public SourceCardView()
        {
            InitializeComponent();
        }

        private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void Card_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && DataContext is SourceCardViewModel vm)
            {
                if (!vm.IsActive) return; // Cannot drag offline devices

                Point current = e.GetPosition(null);
                Vector diff = _dragStartPoint - current;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    DragDrop.DoDragDrop(this, vm.Device, DragDropEffects.Link);
                }
            }
        }
    }
}
