using System;
using System.Windows;
using System.Windows.Controls;

namespace AvasRoutingTool.Controls
{
    /// <summary>
    /// Places each child at a fractional rectangle (0..1) of a canvas, so overlapping multiview windows
    /// (PiP) and gaps (letterboxing) are exact. Children later in the collection are painted on top.
    /// When <see cref="AspectRatio"/> is set the canvas is the largest rectangle of that ratio that fits,
    /// centred - matching how the decoder letterboxes its output.
    /// </summary>
    public class FractionalLayoutPanel : Panel
    {
        public static readonly DependencyProperty XProperty = Register("X");
        public static readonly DependencyProperty YProperty = Register("Y");
        public static readonly DependencyProperty WProperty = Register("W");
        public static readonly DependencyProperty HProperty = Register("H");

        public static readonly DependencyProperty AspectRatioProperty = DependencyProperty.Register(
            nameof(AspectRatio), typeof(double), typeof(FractionalLayoutPanel),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public double AspectRatio
        {
            get => (double)GetValue(AspectRatioProperty);
            set => SetValue(AspectRatioProperty, value);
        }

        public static double GetX(DependencyObject d) => (double)d.GetValue(XProperty);
        public static void SetX(DependencyObject d, double v) => d.SetValue(XProperty, v);
        public static double GetY(DependencyObject d) => (double)d.GetValue(YProperty);
        public static void SetY(DependencyObject d, double v) => d.SetValue(YProperty, v);
        public static double GetW(DependencyObject d) => (double)d.GetValue(WProperty);
        public static void SetW(DependencyObject d, double v) => d.SetValue(WProperty, v);
        public static double GetH(DependencyObject d) => (double)d.GetValue(HProperty);
        public static void SetH(DependencyObject d, double v) => d.SetValue(HProperty, v);

        private static DependencyProperty Register(string name) =>
            DependencyProperty.RegisterAttached(name, typeof(double), typeof(FractionalLayoutPanel),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

        protected override Size MeasureOverride(Size availableSize)
        {
            if (double.IsInfinity(availableSize.Width) || double.IsInfinity(availableSize.Height))
            {
                foreach (UIElement child in InternalChildren) child.Measure(new Size(0, 0));
                return new Size(0, 0);
            }

            var canvas = CanvasRect(availableSize);
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(Math.Max(0, GetW(child) * canvas.Width), Math.Max(0, GetH(child) * canvas.Height)));
            }
            return availableSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var canvas = CanvasRect(finalSize);
            foreach (UIElement child in InternalChildren)
            {
                child.Arrange(new Rect(
                    canvas.X + GetX(child) * canvas.Width,
                    canvas.Y + GetY(child) * canvas.Height,
                    Math.Max(0, GetW(child) * canvas.Width),
                    Math.Max(0, GetH(child) * canvas.Height)));
            }
            return finalSize;
        }

        private Rect CanvasRect(Size space)
        {
            if (AspectRatio <= 0 || space.Width <= 0 || space.Height <= 0)
                return new Rect(0, 0, space.Width, space.Height);

            double width = Math.Min(space.Width, space.Height * AspectRatio);
            double height = width / AspectRatio;
            return new Rect((space.Width - width) / 2, (space.Height - height) / 2, width, height);
        }
    }
}
