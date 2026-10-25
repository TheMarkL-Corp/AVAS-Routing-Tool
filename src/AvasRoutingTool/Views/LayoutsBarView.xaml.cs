using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    public partial class LayoutsBarView : UserControl
    {
        private Point _dragStartPoint;

        public LayoutsBarView()
        {
            InitializeComponent();
        }

        private void LayoutCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void LayoutCard_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement el && el.DataContext is LayoutItemViewModel vm)
            {
                Point current = e.GetPosition(null);
                Vector diff = _dragStartPoint - current;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    DragDrop.DoDragDrop(el, vm, DragDropEffects.Link);
                }
            }
        }
    }
}
