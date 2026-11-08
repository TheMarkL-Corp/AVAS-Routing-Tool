using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AvasRoutingTool.Controls
{
    /// <summary>
    /// A wrap panel whose rows are centred horizontally. A WrapPanel can only align its rows to one side, and the overview's
    /// source strip needs its tiles centred under the header, with every wrapped row (an incomplete last row too) centred.
    /// Children are arranged in logical order, so keyboard Tab order runs left to right, row by row. It does not virtualize:
    /// every child is realized (each source tile owns a live preview).
    /// </summary>
    public class CenteredWrapPanel : Panel
    {
        private sealed class Row
        {
            public readonly List<UIElement> Children = new();
            public double Width;
            public double Height;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var children = VisibleChildren();
            foreach (var child in children)
            {
                child.Measure(availableSize);
            }

            var rows = BuildRows(children, availableSize.Width);
            double widest = rows.Count == 0 ? 0 : rows.Max(r => r.Width);
            double height = rows.Sum(r => r.Height);
            double width = double.IsInfinity(availableSize.Width) ? widest : availableSize.Width;
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var rows = BuildRows(VisibleChildren(), finalSize.Width);
            double y = 0;
            foreach (var row in rows)
            {
                double x = Math.Max(0, (finalSize.Width - row.Width) / 2.0);
                foreach (var child in row.Children)
                {
                    double childWidth = child.DesiredSize.Width;
                    child.Arrange(new Rect(x, y, childWidth, row.Height));
                    x += childWidth;
                }
                y += row.Height;
            }
            return finalSize;
        }

        private List<UIElement> VisibleChildren() =>
            InternalChildren.OfType<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();

        /// <summary>Fills rows left to right in logical order; a child that does not fit starts the next row. An over-wide child gets its own row.</summary>
        private static List<Row> BuildRows(IReadOnlyList<UIElement> children, double availableWidth)
        {
            var rows = new List<Row>();
            var current = new Row();
            bool bounded = !double.IsInfinity(availableWidth);
            foreach (var child in children)
            {
                var size = child.DesiredSize;
                if (current.Children.Count > 0 && bounded && current.Width + size.Width > availableWidth)
                {
                    rows.Add(current);
                    current = new Row();
                }
                current.Children.Add(child);
                current.Width += size.Width;
                current.Height = Math.Max(current.Height, size.Height);
            }
            if (current.Children.Count > 0)
            {
                rows.Add(current);
            }
            return rows;
        }
    }
}
