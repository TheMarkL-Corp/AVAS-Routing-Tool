using System;
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

        /// <summary>The arm banner's Stop: stops placing the armed source (tapping it again), or stops copying.</summary>
        private void StopPlacing_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (vm.ArmedSource is { } armed) vm.ArmSource(armed);
            else if (vm.ArmedDisplay != null) vm.DisarmDisplay();
        }
    }
}
