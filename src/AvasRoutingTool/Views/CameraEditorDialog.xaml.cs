using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Logging;
using AvasRoutingTool.Services.Visca;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    /// <summary>How the camera editor was closed.</summary>
    public enum CameraEditorOutcome
    {
        /// <summary>Cancel, Escape or the close box: nothing to write.</summary>
        Cancelled,
        /// <summary>Save: <see cref="CameraEditorDialog.Result"/> holds the camera to write.</summary>
        Saved,
        /// <summary>The operator confirmed Remove: <see cref="CameraEditorDialog.RemovedCameraId"/> holds the camera.</summary>
        Removed,
    }

    /// <summary>Text of one detect answer, for the list of buttons offered when several answered.</summary>
    public sealed class HitTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is ProbeHit hit ? CameraEditorViewModel.DescribeHit(hit) : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// The camera editor (RC12). DataContext: <see cref="CameraEditorViewModel"/>.
    /// The dialog never writes config: the caller reads <see cref="Outcome"/>, <see cref="Result"/> and <see cref="RemovedCameraId"/>.
    /// Closing it cancels a running Detect or Test connection.
    /// </summary>
    public partial class CameraEditorDialog : Window
    {
        private readonly CameraEditorViewModel _viewModel;
        private readonly string _existingId;
        private readonly CancellationTokenSource _work = new();
        private bool _finished;

        public CameraEditorDialog(CameraEditorViewModel viewModel, string? existingCameraId = null)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _existingId = existingCameraId ?? string.Empty;
            InitializeComponent();
            MaxHeight = SystemParameters.PrimaryScreenHeight;   // the form scrolls when the content is taller than the screen
            DataContext = _viewModel;

            HeaderText.Text = _viewModel.IsNew ? "Add a room camera" : "Edit room camera";
            RemoveButton.Visibility = _viewModel.IsNew ? Visibility.Collapsed : Visibility.Visible;

            _viewModel.PropertyChanged += OnViewModelChanged;
            PreviewKeyDown += OnPreviewKeyDown;
            Closed += OnClosed;
            RefreshBusyAndStatus();
        }

        /// <summary>How the dialog ended (Cancelled until Save or a confirmed Remove).</summary>
        public CameraEditorOutcome Outcome { get; private set; } = CameraEditorOutcome.Cancelled;

        /// <summary>The camera to write when <see cref="Outcome"/> is Saved; null otherwise.</summary>
        public RoomCameraConfig? Result { get; private set; }

        /// <summary>The id of the camera to remove when <see cref="Outcome"/> is Removed; empty otherwise.</summary>
        public string RemovedCameraId { get; private set; } = string.Empty;

        /// <summary>True once Detect or Test connection has been cancelled by closing (tests).</summary>
        internal bool WorkCancelled => _work.IsCancellationRequested;

        // ---- Detect and Test connection ----

        private async void DetectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _viewModel.DetectAsync(_work.Token);
            }
            catch (Exception ex)
            {
                AppLogger.Error("UI", "Camera detect failed", ex);
            }
        }

        private async void TestButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _viewModel.TestConnectionAsync(_work.Token);
            }
            catch (Exception ex)
            {
                AppLogger.Error("UI", "Camera test failed", ex);
            }
        }

        private void HitButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ProbeHit hit })
                _viewModel.ApplyHit(hit);
        }

        private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(CameraEditorViewModel.IsDetecting) or nameof(CameraEditorViewModel.IsTesting)
                or nameof(CameraEditorViewModel.TestText) or nameof(CameraEditorViewModel.IsRemovePending) or nameof(CameraEditorViewModel.Name))
                RefreshBusyAndStatus();
        }

        private void RefreshBusyAndStatus()
        {
            DetectButton.Content = _viewModel.IsDetecting ? "Detecting" : "Detect";
            TestButton.Content = _viewModel.IsTesting ? "Testing" : "Test connection";

            // Status is a dot plus a word, never colour alone: green for a connection, red for no answer, grey while working.
            string words = _viewModel.TestText ?? string.Empty;
            string brush = words.StartsWith("Connected", StringComparison.Ordinal) ? "Brush.Status.Online"
                : words.StartsWith("No answer", StringComparison.Ordinal) ? "Brush.Text.Danger"
                : "Brush.Text.Muted";
            TestDot.Fill = TryFindResource(brush) as Brush ?? Brushes.Transparent;

            RemoveText.Text = $"Remove {_viewModel.Name.Trim()} from the room cameras?";
            RemoveButton.IsEnabled = !_viewModel.IsRemovePending;
        }

        // ---- Save, Cancel, Remove ----

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.IsValid) return;
            Result = _viewModel.BuildConfig();
            Finish(CameraEditorOutcome.Saved, true);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => Finish(CameraEditorOutcome.Cancelled, false);

        private void RemoveButton_Click(object sender, RoutedEventArgs e) => _viewModel.RequestRemove();

        private void KeepButton_Click(object sender, RoutedEventArgs e) => _viewModel.CancelRemove();

        private void ConfirmRemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.ConfirmRemove()) return;
            RemovedCameraId = _existingId;
            Finish(CameraEditorOutcome.Removed, true);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (_viewModel.IsRemovePending) _viewModel.CancelRemove();
            else Finish(CameraEditorOutcome.Cancelled, false);
        }

        private void Finish(CameraEditorOutcome outcome, bool dialogResult)
        {
            if (_finished) return;
            _finished = true;
            Outcome = outcome;
            if (outcome != CameraEditorOutcome.Saved) Result = null;
            try
            {
                DialogResult = dialogResult;   // throws when shown without ShowDialog (tests): Close below ends it either way
            }
            catch (InvalidOperationException)
            {
            }
            Close();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
            // The close box or Alt+F4 while a probe runs: cancel it so no socket outlives the dialog.
            try { _work.Cancel(); }
            catch (Exception ex) { AppLogger.Error("UI", "Camera editor cancel failed", ex); }
            _work.Dispose();
        }
    }
}
