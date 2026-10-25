using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AvasRoutingTool.Controls
{
    /// <summary>
    /// Custom WPF panel that sizes and arranges child cards adaptively to fill the
    /// viewport while preserving a 16:9 video surface aspect ratio and honoring
    /// minimum/maximum readable card bounds.
    /// </summary>
    public class AdaptiveCardPanel : Panel
    {
        public static readonly DependencyProperty SpecProperty =
            DependencyProperty.Register(
                nameof(Spec),
                typeof(GridSpec),
                typeof(AdaptiveCardPanel),
                new FrameworkPropertyMetadata(GridSpec.Default, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public GridSpec Spec
        {
            get => (GridSpec)GetValue(SpecProperty);
            set => SetValue(SpecProperty, value);
        }

        private ScrollViewer? _parentScrollViewer;

        public AdaptiveCardPanel()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            AttachScrollViewer();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DetachScrollViewer();
        }

        private void AttachScrollViewer()
        {
            DetachScrollViewer();
            _parentScrollViewer = FindAncestor<ScrollViewer>(this);
            if (_parentScrollViewer != null)
            {
                _parentScrollViewer.SizeChanged += OnScrollViewerSizeChanged;
            }
        }

        private void DetachScrollViewer()
        {
            if (_parentScrollViewer != null)
            {
                _parentScrollViewer.SizeChanged -= OnScrollViewerSizeChanged;
                _parentScrollViewer = null;
            }
        }

        private void OnScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            InvalidateMeasure();
        }

        private double ResolveEffectiveWidth(double availableWidth)
        {
            if (!double.IsInfinity(availableWidth) && availableWidth > 0)
            {
                return availableWidth;
            }

            EnsureScrollViewerHook();

            if (_parentScrollViewer != null)
            {
                if (_parentScrollViewer.ViewportWidth > 0)
                    return _parentScrollViewer.ViewportWidth;
                if (_parentScrollViewer.ActualWidth > 0)
                    return _parentScrollViewer.ActualWidth;
            }

            return availableWidth;
        }

        private double ResolveEffectiveHeight(double availableHeight)
        {
            if (!double.IsInfinity(availableHeight) && availableHeight > 0)
            {
                return availableHeight;
            }

            EnsureScrollViewerHook();

            if (_parentScrollViewer != null)
            {
                if (_parentScrollViewer.ViewportHeight > 0)
                    return _parentScrollViewer.ViewportHeight;
                if (_parentScrollViewer.ActualHeight > 0)
                    return _parentScrollViewer.ActualHeight;
            }

            return availableHeight;
        }

        private void EnsureScrollViewerHook()
        {
            if (_parentScrollViewer == null)
            {
                _parentScrollViewer = FindAncestor<ScrollViewer>(this);
                if (_parentScrollViewer != null)
                {
                    _parentScrollViewer.SizeChanged += OnScrollViewerSizeChanged;
                }
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var visibleChildren = InternalChildren
                .OfType<UIElement>()
                .Where(c => c.Visibility != Visibility.Collapsed)
                .ToList();

            int count = visibleChildren.Count;
            if (count == 0)
            {
                return new Size(0, 0);
            }

            double width = ResolveEffectiveWidth(availableSize.Width);
            double height = ResolveEffectiveHeight(availableSize.Height);

            var plan = AdaptiveGridLayout.Compute(count, width, height, Spec);

            var childConstraint = new Size(plan.CardWidth, plan.CardHeight);
            foreach (var child in visibleChildren)
            {
                child.Measure(childConstraint);
            }

            double totalWidth = plan.Columns * plan.CardWidth + Math.Max(0, plan.Columns - 1) * Spec.Gap;
            double totalHeight = plan.Rows * plan.CardHeight + Math.Max(0, plan.Rows - 1) * Spec.Gap;

            double resultWidth = double.IsInfinity(availableSize.Width) ? totalWidth : Math.Min(totalWidth, availableSize.Width);
            double resultHeight = totalHeight;

            return new Size(resultWidth, resultHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var visibleChildren = InternalChildren
                .OfType<UIElement>()
                .Where(c => c.Visibility != Visibility.Collapsed)
                .ToList();

            int count = visibleChildren.Count;
            if (count == 0)
            {
                return finalSize;
            }

            double width = finalSize.Width;
            double height = ResolveEffectiveHeight(finalSize.Height);

            var plan = AdaptiveGridLayout.Compute(count, width, height, Spec);
            if (plan.Columns <= 0 || plan.Rows <= 0)
            {
                return finalSize;
            }

            double totalGridWidth = plan.Columns * plan.CardWidth + Math.Max(0, plan.Columns - 1) * Spec.Gap;
            double startX = Math.Max(0, (finalSize.Width - totalGridWidth) / 2.0);

            double totalGridHeight = plan.Rows * plan.CardHeight + Math.Max(0, plan.Rows - 1) * Spec.Gap;
            double startY = Math.Max(0, (height - totalGridHeight) / 2.0);

            for (int i = 0; i < count; i++)
            {
                int col = i % plan.Columns;
                int row = i / plan.Columns;

                double x = startX + col * (plan.CardWidth + Spec.Gap);
                double y = startY + row * (plan.CardHeight + Spec.Gap);

                visibleChildren[i].Arrange(new Rect(x, y, plan.CardWidth, plan.CardHeight));
            }

            return finalSize;
        }

        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(current);
            while (parent != null)
            {
                if (parent is T typed) return typed;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}
