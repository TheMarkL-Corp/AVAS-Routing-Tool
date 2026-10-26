using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace AvasRoutingTool.Views
{
    /// <summary>
    /// The copy drag's ghost (spec 15): the source's mini picture following the pointer, drawn in the window's adorner
    /// layer. It is part of the window, not a window of its own, and it is not hit-testable, so it can never become
    /// the drop target, even where a separate popup window would be clamped back under the pointer at a screen edge.
    /// </summary>
    internal sealed class CopyGhostAdorner : Adorner
    {
        /// <summary>The ghost sits this far right of and below the pointer.</summary>
        public const double PointerOffset = 18;

        private readonly FrameworkElement _ghost;
        private Point _position;

        public CopyGhostAdorner(UIElement adornedRoot, FrameworkElement ghost) : base(adornedRoot)
        {
            _ghost = ghost;
            _ghost.IsHitTestVisible = false;
            IsHitTestVisible = false;
            AddVisualChild(_ghost);
        }

        /// <summary>The ghost's top-left corner, in the adorned root's coordinates.</summary>
        public Point Position => _position;

        /// <summary>The ghost's area, in the adorned root's coordinates.</summary>
        public Rect Bounds => new(_position, _ghost.RenderSize);

        /// <summary>Places the ghost beside the pointer (given in the adorned root's coordinates).</summary>
        public void MoveTo(Point pointer)
        {
            _position = new Point(pointer.X + PointerOffset, pointer.Y + PointerOffset);
            InvalidateArrange();
        }

        /// <summary>Lets go of the hosted element (the drag is over).</summary>
        public void Detach() => RemoveVisualChild(_ghost);

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => _ghost;

        protected override Size MeasureOverride(Size constraint)
        {
            _ghost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return base.MeasureOverride(constraint);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _ghost.Arrange(new Rect(_position, _ghost.DesiredSize));
            return finalSize;
        }
    }
}
