using System;
using System.Windows;
using System.Windows.Controls;

namespace AvasRoutingTool.Controls
{
    /// <summary>
    /// Layout of the step header: marker, label, done check, left to right and centred on one line. The marker and the check keep their size;
    /// the label (second child) takes the width that is left and wraps, so the check sits right after the label text and never leaves the host.
    /// </summary>
    public class StepHeaderPanel : Panel
    {
        protected override Size MeasureOverride(Size available)
        {
            if (InternalChildren.Count < 3) return new Size();
            var marker = InternalChildren[0];
            var label = InternalChildren[1];
            var check = InternalChildren[2];
            var open = new Size(double.PositiveInfinity, double.PositiveInfinity);
            marker.Measure(open);
            check.Measure(open);
            double room = double.IsInfinity(available.Width)
                ? double.PositiveInfinity
                : Math.Max(0, available.Width - marker.DesiredSize.Width - check.DesiredSize.Width);
            label.Measure(new Size(room, double.PositiveInfinity));
            double height = Math.Max(marker.DesiredSize.Height, Math.Max(label.DesiredSize.Height, check.DesiredSize.Height));
            return new Size(marker.DesiredSize.Width + label.DesiredSize.Width + check.DesiredSize.Width, height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            if (InternalChildren.Count < 3) return final;
            double x = 0;
            foreach (UIElement child in InternalChildren)
            {
                var size = child.DesiredSize;
                child.Arrange(new Rect(x, Math.Max(0, (final.Height - size.Height) / 2), size.Width, size.Height));
                x += size.Width;
            }
            return final;
        }
    }
}
