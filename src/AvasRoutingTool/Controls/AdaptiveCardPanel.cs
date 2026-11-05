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

        /// <summary>
        /// Whether a grid shorter than the viewport is centred vertically (the default). The Displays panel turns it off,
        /// so its cards start right under the section header.
        /// </summary>
        public static readonly DependencyProperty CenterVerticallyProperty =
            DependencyProperty.Register(
                nameof(CenterVertically),
                typeof(bool),
                typeof(AdaptiveCardPanel),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsArrange));

        public bool CenterVertically
        {
            get => (bool)GetValue(CenterVerticallyProperty);
            set => SetValue(CenterVerticallyProperty, value);
        }
        private ScrollViewer? _parentScrollViewer;
        private GridPlan? _carouselPlan;
        private double _carouselViewport;
        private int _carouselCount;

        private bool IsCarousel => Spec.CarouselVisibleCards > 0;

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
                HookScrollViewer(_parentScrollViewer);
            }
        }

        private void DetachScrollViewer()
        {
            if (_parentScrollViewer != null)
            {
                _parentScrollViewer.SizeChanged -= OnScrollViewerSizeChanged;
                _parentScrollViewer.ScrollChanged -= OnScrollViewerScrollChanged;
                _parentScrollViewer = null;
            }
        }

        private void HookScrollViewer(ScrollViewer sv)
        {
            sv.SizeChanged += OnScrollViewerSizeChanged;
            sv.ScrollChanged += OnScrollViewerScrollChanged;
        }

        // The viewport width settles after the scroller's SizeChanged: a carousel row must be re-planned when it changes.
        private void OnScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (IsCarousel && e.ViewportWidthChange != 0) InvalidateMeasure();
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
                    HookScrollViewer(_parentScrollViewer);
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
            if (IsCarousel)
            {
                _carouselPlan = plan;
                _carouselViewport = width;
                _carouselCount = count;
            }

            var childConstraint = new Size(plan.CardWidth, plan.CardHeight);
            foreach (var child in visibleChildren)
            {
                child.Measure(childConstraint);
            }

            double totalWidth = plan.Columns * plan.CardWidth + Math.Max(0, plan.Columns - 1) * Spec.Gap;
            double totalHeight = plan.Rows * plan.CardHeight + Math.Max(0, plan.Rows - 1) * Spec.Gap;

            // Carousel rows report their full width so a horizontal ScrollViewer can scroll them.
            double resultWidth = IsCarousel || double.IsInfinity(availableSize.Width)
                ? totalWidth
                : Math.Min(totalWidth, availableSize.Width);
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

            double viewport = width;
            GridPlan plan;
            if (IsCarousel)
            {
                // finalSize is the (wider) row, so the plan must come from the viewport, not from it.
                double v = ResolveEffectiveWidth(double.PositiveInfinity);
                viewport = double.IsInfinity(v) || v <= 0 ? finalSize.Width : v;
                if (_carouselPlan is { } stored && _carouselCount == count && Math.Abs(_carouselViewport - viewport) < 0.01)
                {
                    plan = stored;
                }
                else
                {
                    plan = AdaptiveGridLayout.Compute(count, viewport, height, Spec);
                    _carouselPlan = plan;
                    _carouselViewport = viewport;
                    _carouselCount = count;
                }
            }
            else
            {
                plan = AdaptiveGridLayout.Compute(count, width, height, Spec);
            }
            if (plan.Columns <= 0 || plan.Rows <= 0)
            {
                return finalSize;
            }

            double totalGridWidth = plan.Columns * plan.CardWidth + Math.Max(0, plan.Columns - 1) * Spec.Gap;
            double startX = Math.Max(0, (viewport - totalGridWidth) / 2.0);

            double totalGridHeight = plan.Rows * plan.CardHeight + Math.Max(0, plan.Rows - 1) * Spec.Gap;
            double startY = CenterVertically ? Math.Max(0, (height - totalGridHeight) / 2.0) : 0;

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
