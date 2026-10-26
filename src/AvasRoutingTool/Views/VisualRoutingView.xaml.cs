using System;
using System.Windows;
using System.Windows.Controls;
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

        /// <summary>The arm banner's Stop: tapping the armed source again disarms it.</summary>
        private void StopPlacing_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel { ArmedSource: { } armed } vm) vm.ArmSource(armed);
        }
    }
}
