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
        private bool _pressed;
        private bool _dragged;

        public SourceCardView()
        {
            InitializeComponent();
        }

        private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _pressed = true;
            _dragged = false;
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
                    _dragged = true;
                    DragDrop.DoDragDrop(this, vm.Device, DragDropEffects.Link);
                }
            }
        }

        /// <summary>
        /// A tap (press and release without a drag) arms the source: the overview's MainViewModel.ArmSource toggles it
        /// (inside the Composer it arms it there). The card's own buttons handle their release, so they never arm.
        /// </summary>
        private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            bool tap = _pressed && !_dragged;
            _pressed = false;
            _dragged = false;
            if (!tap || DataContext is not SourceCardViewModel vm || !vm.IsActive) return; // offline sources cannot be placed, as they cannot be dragged

            ViewLookup.FindMainViewModel(this)?.ArmSource(vm);
        }
    }
}
