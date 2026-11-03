using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    /// <summary>
    /// Tap versus drag for one pointer (spec 6): a drag starts only after more than <see cref="DragThreshold"/> px of
    /// movement while pressed; a release without a drag is a tap. Pure, so it is unit-tested without WPF input.
    /// </summary>
    public sealed class TapGesture
    {
        public const double DragThreshold = 8;

        private Point _start;
        private bool _pressed;
        private bool _dragging;

        /// <summary>A press is held here (a later move may still start a drag).</summary>
        public bool IsPressed => _pressed;

        public void Press(Point at)
        {
            _start = at;
            _pressed = true;
            _dragging = false;
        }

        /// <summary>True exactly once: when the movement first exceeds the threshold (start the drag now).</summary>
        public bool Move(Point to)
        {
            if (!_pressed || _dragging) return false;
            if (Math.Abs(to.X - _start.X) <= DragThreshold && Math.Abs(to.Y - _start.Y) <= DragThreshold) return false;
            _dragging = true;
            return true;
        }

        /// <summary>True when this release ends a tap (pressed here, never dragged).</summary>
        public bool Release()
        {
            bool tap = _pressed && !_dragging;
            _pressed = false;
            _dragging = false;
            return tap;
        }
    }

    /// <summary>Tap-to-arm and drag-to-route for both source card views (the overview card and the Composer's compact palette).</summary>
    internal static class SourceCardInput
    {
        /// <summary>
        /// A press starts tracking a tap (or a drag). A press on one of the tile's own buttons (the rename pencil, Start, Stop, Refresh)
        /// is that button's: it neither becomes a tap on the tile nor can a move after it start a drag of the source.
        /// </summary>
        public static void Down(TapGesture gesture, UIElement card, MouseButtonEventArgs e)
        {
            if (IsOnAButton(e.OriginalSource as DependencyObject, card))
            {
                gesture.Release(); // forget any earlier press that never saw its release
                return;
            }
            gesture.Press(e.GetPosition(card));
        }

        private static bool IsOnAButton(DependencyObject? d, DependencyObject card)
        {
            for (; d != null && !ReferenceEquals(d, card); d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            {
                if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
            }
            return false;
        }

        public static void Move(TapGesture gesture, FrameworkElement card, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || card.DataContext is not SourceCardViewModel vm || !vm.IsActive) return;
            if (gesture.Move(e.GetPosition(card)))
                DragDrop.DoDragDrop(card, vm.Device, DragDropEffects.Link);
        }

        /// <summary>
        /// Enter or Space on the focused card arms the source, as a tap does (and again stops it). Only the card itself
        /// handles this: Enter on one of its own buttons is that button's.
        /// </summary>
        public static void KeyDown(FrameworkElement card, KeyEventArgs e)
        {
            if (e.Key is not (Key.Enter or Key.Space) || !ReferenceEquals(e.OriginalSource, card)) return;
            e.Handled = true;
            if (e.IsRepeat) return; // a held key is one press: it must not arm and disarm over and over
            if (card.DataContext is SourceCardViewModel { IsActive: true } vm) ViewLookup.FindMainViewModel(card)?.ArmSource(vm);
        }

        /// <summary>
        /// A tap arms the source (MainViewModel.ArmSource toggles it; inside the Composer it arms it there). The card's
        /// own buttons handle their release, so they never arm. Offline sources cannot be placed, as they cannot be dragged.
        /// </summary>
        public static void Up(TapGesture gesture, FrameworkElement card)
        {
            if (!gesture.Release() || card.DataContext is not SourceCardViewModel vm || !vm.IsActive) return;
            ViewLookup.FindMainViewModel(card)?.ArmSource(vm);
        }
    }
}
