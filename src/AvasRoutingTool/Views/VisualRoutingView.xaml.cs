using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AvasRoutingTool.Logging;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    public partial class VisualRoutingView : UserControl
    {
        /// <summary>How often the overview's time-based housekeeping runs (arm idle, drop choice, Undo windows).</summary>
        public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

        private readonly DispatcherTimer _timer;

        public VisualRoutingView()
        {
            InitializeComponent();

            // The only UI timer: MainViewModel.Tick while the view is loaded.
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TickInterval };
            _timer.Tick += OnTick;
            Loaded += (_, _) => _timer.Start();
            Unloaded += (_, _) => _timer.Stop();
            DataContextChanged += (_, e) => Watch(e.OldValue as MainViewModel, e.NewValue as MainViewModel);
        }

        /// <summary>
        /// The arm banners (polite) and the rename error (assertive) are live regions, but WPF does not raise a live-region change when a bound text
        /// changes: a screen reader hears them only when they are announced here.
        /// </summary>
        private void Watch(MainViewModel? old, MainViewModel? main)
        {
            if (old != null) old.PropertyChanged -= OnMainChanged;
            if (main != null) main.PropertyChanged += OnMainChanged;
        }

        /// <summary>What each banner said when it was last announced: a change notification with the same text (a copy arm raises it twice) is not read out again.</summary>
        private string? _announcedArm;
        private string? _announcedCopy;

        private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not MainViewModel main) return;
            if (e.PropertyName == nameof(MainViewModel.ArmBannerText))
            {
                string? text = main.ArmBannerText;
                if (text != null && text != _announcedArm) Motion.Announce(ArmBannerLabel);
                _announcedArm = text;
            }
            else if (e.PropertyName == nameof(MainViewModel.CopyArmBanner))
            {
                string? text = main.CopyArmBanner;
                if (text != null && text != _announcedCopy) Motion.Announce(CopyArmBannerLabel);
                _announcedCopy = text;
            }
            else if (e.PropertyName == nameof(MainViewModel.RenameBanner))
            {
                // An error: assertive. A failure with the same words as the last one is a new failure, so it is read again.
                if (main.HasRenameBanner) Motion.Announce(RenameBannerText);
            }
        }

        /// <summary>The Tick timer runs while the view is loaded (tests).</summary>
        internal bool IsTimerRunning => _timer.IsEnabled;

        private void OnTick(object? sender, EventArgs e)
        {
            try
            {
                (DataContext as MainViewModel)?.Tick();
            }
            catch (Exception ex)
            {
                AppLogger.Error("UI", "Tick failed", ex);
            }
        }

        /// <summary>
        /// Esc, from anywhere in the overview or the Composer, stops what is armed one step at a time (the Composer's
        /// ladder while it is open) and never closes the Composer. Keys a control already used arrive handled.
        /// </summary>
        private void Root_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled || e.Key != Key.Escape || DataContext is not MainViewModel vm) return;
            if (vm.Escape()) e.Handled = true;
        }

        /// <summary>Gap between two display cards (the display card spec's gap): one arrow press moves one card width plus this.</summary>
        private static double CardGap => Controls.GridSpec.DisplayCard.Gap;

        /// <summary>
        /// Scrolls the displays row sideways by whole cards (one card width plus the gap each); positive = right. Clamped to both ends.
        /// </summary>
        public void ScrollDisplaysBy(int cards)
        {
            double step = DisplaysCardStep();
            if (step <= 0) return;
            double target = Math.Max(0, Math.Min(DisplaysScroll.ScrollableWidth, DisplaysScroll.HorizontalOffset + cards * step));
            DisplaysScroll.ScrollToHorizontalOffset(target);
            // The scroll bar and the arrows read the new offset after layout; bring the arrows up to date now.
            UpdateDisplayArrows(target);
        }

        private double DisplaysCardStep()
        {
            var card = FindFirst<DisplayCardView>(DisplaysScroll);
            return card == null || card.ActualWidth <= 0 ? 0 : card.ActualWidth + CardGap;
        }

        private void DisplaysScrollLeft_Click(object sender, RoutedEventArgs e) => ScrollDisplaysBy(-1);

        private void DisplaysScrollRight_Click(object sender, RoutedEventArgs e) => ScrollDisplaysBy(1);

        /// <summary>The mouse wheel over the row pans it sideways (the row has no vertical direction to scroll).</summary>
        private void DisplaysScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Nothing to pan sideways (a short window scrolls the row vertically): leave the wheel to the ScrollViewer.
            if (DisplaysScroll.ScrollableWidth <= 0.5) return;
            double target = Math.Max(0, Math.Min(DisplaysScroll.ScrollableWidth, DisplaysScroll.HorizontalOffset - e.Delta));
            DisplaysScroll.ScrollToHorizontalOffset(target);
            UpdateDisplayArrows(target);
            e.Handled = true;
        }

        private void DisplaysScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateDisplayArrows(DisplaysScroll.HorizontalOffset);

        private void DisplaysScroll_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateDisplayArrows(DisplaysScroll.HorizontalOffset);

        /// <summary>Shows the arrows only while the row scrolls, disables each at its end, and centres them on the card pictures.</summary>
        private void UpdateDisplayArrows(double offset)
        {
            double scrollable = DisplaysScroll.ScrollableWidth;
            bool leftHadFocus = DisplaysScrollLeft.IsKeyboardFocused;
            bool rightHadFocus = DisplaysScrollRight.IsKeyboardFocused;
            DisplaysScrollLeft.IsEnabled = offset > 0.5;
            DisplaysScrollRight.IsEnabled = offset < scrollable - 0.5;
            if (scrollable <= 0.5)
            {
                DisplaysScrollLeft.Visibility = Visibility.Collapsed;
                DisplaysScrollRight.Visibility = Visibility.Collapsed;
                return;
            }

            // Vertically centred on the first card's picture (every card is the same height). Until that is known the arrows stay hidden.
            var card = FindFirst<DisplayCardView>(DisplaysScroll);
            var picture = card == null ? null : FindFirst<FrameworkElement>(card, f => f.Name == "PreviewSurface");
            if (picture == null || picture.ActualHeight <= 0 || !picture.IsDescendantOf(DisplaysScroll))
            {
                DisplaysScrollLeft.Visibility = Visibility.Collapsed;
                DisplaysScrollRight.Visibility = Visibility.Collapsed;
                return;
            }
            DisplaysScrollLeft.Visibility = Visibility.Visible;
            DisplaysScrollRight.Visibility = Visibility.Visible;

            // A focused arrow that just disabled itself hands the keyboard to the other arrow, else to the nearest card.
            if (leftHadFocus && !DisplaysScrollLeft.IsEnabled) MoveFocusFromArrow(DisplaysScrollRight, first: true);
            else if (rightHadFocus && !DisplaysScrollRight.IsEnabled) MoveFocusFromArrow(DisplaysScrollLeft, first: false);
            var bounds = picture.TransformToAncestor(DisplaysScroll).TransformBounds(new Rect(0, 0, picture.ActualWidth, picture.ActualHeight));
            double centre = bounds.Top + bounds.Height / 2;
            double top = Math.Max(0, centre + DisplaysScroll.Margin.Top - DisplaysScrollLeft.Height / 2);
            if (Math.Abs(DisplaysScrollLeft.Margin.Top - top) > 0.5)
            {
                DisplaysScrollLeft.Margin = new Thickness(DisplaysScrollLeft.Margin.Left, top, 0, 0);
                DisplaysScrollRight.Margin = new Thickness(0, top, DisplaysScrollRight.Margin.Right, 0);
            }
        }

        private void MoveFocusFromArrow(Button other, bool first)
        {
            if (other.IsEnabled && other.IsVisible && other.Focus()) return;
            var cards = new System.Collections.Generic.List<DisplayCardView>();
            CollectAll(DisplaysScroll, cards);
            var card = cards.Count == 0 ? null : first ? cards[0] : cards[cards.Count - 1];
            if (card == null || !card.MoveFocus(new TraversalRequest(first ? FocusNavigationDirection.First : FocusNavigationDirection.Last)))
                DisplaysScroll.Focus();
        }

        private static void CollectAll<T>(DependencyObject root, System.Collections.Generic.List<T> into) where T : DependencyObject
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T t) into.Add(t);
                else CollectAll(child, into);
            }
        }

        private static T? FindFirst<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T t && (match == null || match(t))) return t;
                var nested = FindFirst(child, match);
                if (nested != null) return nested;
            }
            return null;
        }

        /// <summary>The arm banner's Stop: stops placing the armed source (tapping it again), or stops copying.</summary>
        private void StopPlacing_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (vm.ArmedSource is { } armed) vm.ArmSource(armed);
            else if (vm.ArmedDisplay != null) vm.DisarmDisplay();
        }
    }
}
