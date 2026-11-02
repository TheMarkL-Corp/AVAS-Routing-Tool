using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using AvasRoutingTool.Logging;
using AvasRoutingTool.Services.Visca;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    /// <summary>
    /// The Room Cameras tab (RC11). DataContext: <see cref="RoomCamerasViewModel"/>.
    /// Owns the housekeeping timer (while loaded), the Stop-on-hide / unload / focus-loss rules and the keyboard (R14).
    /// The camera editor (RC12) opens by default; a host that subscribes to <see cref="EditorRequested"/> takes over.
    /// </summary>
    public partial class RoomCamerasView : UserControl
    {
        /// <summary>The same cadence as the overview's Tick: motion watchdog, Stop retries, polls and slider throttle run on it.</summary>
        public static readonly TimeSpan TickInterval = VisualRoutingView.TickInterval;

        private readonly DispatcherTimer _timer;
        private readonly HashSet<Key> _held = new();
        private Key? _zoomKey;
        private bool _zoomDragging;
        private bool _focusDragging;
        private RoomCamerasViewModel? _watched;
        private Window? _window;

        public RoomCamerasView()
        {
            InitializeComponent();

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TickInterval };
            _timer.Tick += OnTick;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += OnIsVisibleChanged;
            DataContextChanged += (_, e) => Watch(e.OldValue as RoomCamerasViewModel, e.NewValue as RoomCamerasViewModel);
            IsKeyboardFocusWithinChanged += (_, e) => { if (!(bool)e.NewValue) StopAll(); };
            PreviewKeyDown += OnPreviewKeyDown;
            PreviewKeyUp += OnPreviewKeyUp;
        }

        /// <summary>
        /// An optional host override for opening the camera editor: a camera to edit, or null for a new one (from the Add chip, the pencil,
        /// "Bind a source" and the empty state).
        /// </summary>
        public event Action<RoomCameraViewModel?>? EditorRequested;

        /// <summary>The Tick timer runs while the view is loaded (tests).</summary>
        internal bool IsTimerRunning => _timer.IsEnabled;

        private RoomCamerasViewModel? ViewModel => DataContext as RoomCamerasViewModel;

        // ---- Timer and lifetime ----

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _timer.Start();
            _window = Window.GetWindow(this);
            if (_window != null) _window.Deactivated += OnWindowDeactivated;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            if (_window != null) _window.Deactivated -= OnWindowDeactivated;
            _window = null;
            StopAll();
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) StopAll();
        }

        private void OnWindowDeactivated(object? sender, EventArgs e) => StopAll();

        private void OnTick(object? sender, EventArgs e)
        {
            try
            {
                ViewModel?.Tick();
            }
            catch (Exception ex)
            {
                AppLogger.Error("UI", "Room cameras tick failed", ex);
            }
        }

        private void StopAll()
        {
            _held.Clear();
            _zoomKey = null;
            _zoomDragging = false;
            _focusDragging = false;
            try
            {
                ViewModel?.StopAll();
            }
            catch (Exception ex)
            {
                AppLogger.Error("UI", "Room cameras stop failed", ex);
            }
        }

        private void Watch(RoomCamerasViewModel? old, RoomCamerasViewModel? vm)
        {
            if (old != null)
            {
                old.AddRequested -= OnAddRequested;
                old.EditRequested -= OnEditRequested;
            }
            _watched = vm;
            if (vm != null)
            {
                vm.AddRequested += OnAddRequested;
                vm.EditRequested += OnEditRequested;
            }
        }

        private void OnAddRequested() => RaiseEditorRequested(null);

        private void OnEditRequested(RoomCameraViewModel camera) => RaiseEditorRequested(camera);

        /// <summary>
        /// A host that subscribes to <see cref="EditorRequested"/> handles the request itself; otherwise the view opens the camera editor
        /// (<see cref="ShowEditor"/>) and applies the outcome to its view model.
        /// </summary>
        protected virtual void RaiseEditorRequested(RoomCameraViewModel? camera)
        {
            if (EditorRequested != null)
            {
                EditorRequested.Invoke(camera);
                return;
            }
            OpenEditor(camera);
        }

        private void OpenEditor(RoomCameraViewModel? camera)
        {
            var vm = ViewModel;
            if (vm == null) return;
            try
            {
                var dialog = new CameraEditorDialog(vm.CreateEditor(camera), camera?.Id);
                ShowEditor(dialog);
                switch (dialog.Outcome)
                {
                    case CameraEditorOutcome.Saved when dialog.Result is { } config:
                        if (camera == null) vm.AddCamera(config);
                        else vm.UpdateCamera(config);
                        break;
                    case CameraEditorOutcome.Removed when !string.IsNullOrEmpty(dialog.RemovedCameraId):
                        vm.RemoveCamera(dialog.RemovedCameraId);
                        break;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("UI", "Camera editor failed", ex);
            }
        }

        /// <summary>
        /// Test hook: replaces how the editor is shown (a XAML view cannot be subclassed by a test, because its component URI is
        /// bound to the declaring type). When set, it must show, drive and close the dialog.
        /// </summary>
        internal Action<CameraEditorDialog>? EditorPresenter { get; set; }

        /// <summary>Seam: shows the editor modally over the hosting window and returns when it is closed.</summary>
        protected virtual void ShowEditor(CameraEditorDialog dialog)
        {
            if (EditorPresenter != null)
            {
                EditorPresenter(dialog);
                return;
            }
            var owner = Window.GetWindow(this);
            if (owner != null) dialog.Owner = owner;
            dialog.ShowDialog();
        }

        // ---- Sliders: begin / end of a drag (final value is sent on release) ----

        private RoomCameraViewModel? Camera => (_watched ?? ViewModel)?.Selected;

        private void ZoomSlider_DragStart(object sender, RoutedEventArgs e)
        {
            if (_zoomDragging || Camera is not { ZoomEnabled: true } camera) return;
            _zoomDragging = true;
            camera.BeginZoomDrag();
        }

        private void ZoomSlider_DragEnd(object sender, RoutedEventArgs e)
        {
            if (!_zoomDragging) return;
            _zoomDragging = false;
            Camera?.EndZoomDrag();
        }

        private void FocusSlider_DragStart(object sender, RoutedEventArgs e)
        {
            if (_focusDragging || Camera is not { FocusEnabled: true } camera) return;
            _focusDragging = true;
            camera.BeginFocusDrag();
        }

        private void FocusSlider_DragEnd(object sender, RoutedEventArgs e)
        {
            if (!_focusDragging) return;
            _focusDragging = false;
            Camera?.EndFocusDrag();
        }

        // ---- Keyboard (R14) ----

        /// <summary>
        /// Keys act only while focus is inside this view and not in a text field, an editable combo box or a slider
        /// (which uses the arrow keys itself).
        /// </summary>
        private bool KeysApply()
        {
            if (!IsKeyboardFocusWithin) return false;
            return Keyboard.FocusedElement switch
            {
                TextBoxBase => false,
                ComboBox { IsEditable: true } => false,
                Slider => false,
                _ => true,
            };
        }

        private static bool IsArrow(Key k) => k is Key.Up or Key.Down or Key.Left or Key.Right;

        private static ZoomDirection? ZoomOf(Key k) => k switch
        {
            Key.OemPlus or Key.Add => ZoomDirection.Tele,
            Key.OemMinus or Key.Subtract => ZoomDirection.Wide,
            _ => null,
        };

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (!KeysApply() || Camera is not { } camera) return;

            if (key == Key.Escape)
            {
                StopAll();
                e.Handled = true;
            }
            else if (key == Key.Home)
            {
                if (camera.HomeCommand.CanExecute(null)) camera.HomeCommand.Execute(null);
                e.Handled = true;
            }
            else if (IsArrow(key))
            {
                e.Handled = true;
                if (_held.Add(key)) ApplyArrows(camera);
            }
            else if (ZoomOf(key) is { } zoom)
            {
                e.Handled = true;
                if (_zoomKey != key)
                {
                    _zoomKey = key;
                    camera.BeginZoom(zoom);
                }
            }
        }

        private void OnPreviewKeyUp(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            // Key up always ends motion, even if focus has moved meanwhile: Stop is never skipped.
            var camera = Camera;
            if (camera == null) return;

            if (IsArrow(key) && _held.Remove(key))
            {
                e.Handled = true;
                ApplyArrows(camera);
            }
            else if (_zoomKey == key)
            {
                e.Handled = true;
                _zoomKey = null;
                camera.EndZoom();
                if (_held.Count > 0) ApplyArrows(camera);   // End stops both axes: resume the pan/tilt that is still held
            }
        }

        /// <summary>Held arrows give one direction (two arrows make a diagonal, opposite arrows cancel); none held ends the motion.</summary>
        private void ApplyArrows(RoomCameraViewModel camera)
        {
            bool up = _held.Contains(Key.Up), down = _held.Contains(Key.Down);
            bool left = _held.Contains(Key.Left), right = _held.Contains(Key.Right);
            if (up && down) up = down = false;
            if (left && right) left = right = false;

            PanTiltDirection? direction = (up, down, left, right) switch
            {
                (true, _, true, _) => PanTiltDirection.UpLeft,
                (true, _, _, true) => PanTiltDirection.UpRight,
                (_, true, true, _) => PanTiltDirection.DownLeft,
                (_, true, _, true) => PanTiltDirection.DownRight,
                (true, _, _, _) => PanTiltDirection.Up,
                (_, true, _, _) => PanTiltDirection.Down,
                (_, _, true, _) => PanTiltDirection.Left,
                (_, _, _, true) => PanTiltDirection.Right,
                _ => null,
            };

            if (direction is { } d) camera.BeginMove(d);
            else camera.EndMove();
        }
    }
}
