using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace AvasRoutingTool.Views
{
    /// <summary>
    /// Press-and-hold for any <see cref="ButtonBase"/> (RC10, R4). <see cref="PressCommand"/> runs once when the button goes down
    /// (mouse, touch, or Space/Enter key down, never on auto-repeat). <see cref="ReleaseCommand"/> runs exactly once per press, on
    /// the first of: mouse up, touch up, lost mouse or touch capture, lost keyboard focus, key up (keyboard presses), the button
    /// being disabled or unloaded, or the owning window being deactivated. A release without a press runs nothing.
    /// Both commands receive <see cref="CommandParameter"/> as their parameter.
    /// </summary>
    public static class HoldButton
    {
        public static readonly DependencyProperty PressCommandProperty = DependencyProperty.RegisterAttached(
            "PressCommand", typeof(ICommand), typeof(HoldButton), new PropertyMetadata(null, OnBindingChanged));

        public static readonly DependencyProperty ReleaseCommandProperty = DependencyProperty.RegisterAttached(
            "ReleaseCommand", typeof(ICommand), typeof(HoldButton), new PropertyMetadata(null, OnBindingChanged));

        public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.RegisterAttached(
            "CommandParameter", typeof(object), typeof(HoldButton), new PropertyMetadata(null));

        private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
            "State", typeof(HoldState), typeof(HoldButton), new PropertyMetadata(null));

        public static ICommand? GetPressCommand(ButtonBase element) => (ICommand?)element.GetValue(PressCommandProperty);
        public static void SetPressCommand(ButtonBase element, ICommand? value) => element.SetValue(PressCommandProperty, value);

        public static ICommand? GetReleaseCommand(ButtonBase element) => (ICommand?)element.GetValue(ReleaseCommandProperty);
        public static void SetReleaseCommand(ButtonBase element, ICommand? value) => element.SetValue(ReleaseCommandProperty, value);

        public static object? GetCommandParameter(ButtonBase element) => element.GetValue(CommandParameterProperty);
        public static void SetCommandParameter(ButtonBase element, object? value) => element.SetValue(CommandParameterProperty, value);

        private static void OnBindingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ButtonBase button)
                Refresh(button);
        }

        /// <summary>Attaches the handlers while a press or a release command is set; detaches them all once both are cleared.</summary>
        private static void Refresh(ButtonBase button)
        {
            var state = (HoldState?)button.GetValue(StateProperty);
            var wanted = GetPressCommand(button) != null || GetReleaseCommand(button) != null;
            if (wanted && state == null)
            {
                state = new HoldState(button);
                button.SetValue(StateProperty, state);
                state.Attach();
            }
            else if (!wanted && state != null)
            {
                button.ClearValue(StateProperty);
                state.Detach();
            }
        }

        /// <summary>The per-button press bookkeeping and the handlers that drive it.</summary>
        private sealed class HoldState
        {
            private enum PressSource { None, Mouse, Touch, Keyboard }

            private readonly ButtonBase _button;
            private PressSource _source = PressSource.None;
            private TouchDevice? _touch;
            private Window? _window;

            public HoldState(ButtonBase button)
            {
                _button = button;
            }

            public void Attach()
            {
                _button.PreviewMouseLeftButtonDown += OnMouseDown;
                _button.PreviewMouseLeftButtonUp += OnMouseUp;
                _button.LostMouseCapture += OnLostMouseCapture;
                _button.PreviewTouchDown += OnTouchDown;
                _button.PreviewTouchUp += OnTouchUp;
                _button.LostTouchCapture += OnLostTouchCapture;
                _button.PreviewKeyDown += OnKeyDown;
                _button.PreviewKeyUp += OnKeyUp;
                _button.LostKeyboardFocus += OnLostKeyboardFocus;
                _button.IsEnabledChanged += OnIsEnabledChanged;
                _button.Unloaded += OnUnloaded;
            }

            public void Detach()
            {
                EndPress();
                _button.PreviewMouseLeftButtonDown -= OnMouseDown;
                _button.PreviewMouseLeftButtonUp -= OnMouseUp;
                _button.LostMouseCapture -= OnLostMouseCapture;
                _button.PreviewTouchDown -= OnTouchDown;
                _button.PreviewTouchUp -= OnTouchUp;
                _button.LostTouchCapture -= OnLostTouchCapture;
                _button.PreviewKeyDown -= OnKeyDown;
                _button.PreviewKeyUp -= OnKeyUp;
                _button.LostKeyboardFocus -= OnLostKeyboardFocus;
                _button.IsEnabledChanged -= OnIsEnabledChanged;
                _button.Unloaded -= OnUnloaded;
            }

            private void OnMouseDown(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.Left && BeginPress(PressSource.Mouse))
                    _button.CaptureMouse();
            }

            private void OnMouseUp(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.Left && _source == PressSource.Mouse)
                    EndPress();
            }

            private void OnLostMouseCapture(object sender, MouseEventArgs e) => EndPress();

            private void OnTouchDown(object? sender, TouchEventArgs e)
            {
                if (BeginPress(PressSource.Touch))
                {
                    _touch = e.TouchDevice;
                    _button.CaptureTouch(e.TouchDevice);
                }
            }

            private void OnTouchUp(object? sender, TouchEventArgs e)
            {
                if (_source == PressSource.Touch)
                    EndPress();
            }

            private void OnLostTouchCapture(object? sender, TouchEventArgs e) => EndPress();

            private void OnKeyDown(object sender, KeyEventArgs e)
            {
                if (e.IsRepeat || !IsPressKey(e.Key))
                    return;
                BeginPress(PressSource.Keyboard);
            }

            private void OnKeyUp(object sender, KeyEventArgs e)
            {
                if (_source == PressSource.Keyboard && IsPressKey(e.Key))
                    EndPress();
            }

            private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => EndPress();

            private void OnIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
            {
                if (!_button.IsEnabled)
                    EndPress();
            }

            private void OnUnloaded(object sender, RoutedEventArgs e) => EndPress();

            private void OnWindowDeactivated(object? sender, System.EventArgs e) => EndPress();

            private static bool IsPressKey(Key key) => key is Key.Space or Key.Enter;

            /// <summary>Starts a press unless one is already active or the button is disabled. Returns true when this call started it.</summary>
            private bool BeginPress(PressSource source)
            {
                if (_source != PressSource.None || !_button.IsEnabled)
                    return false;

                _source = source;
                _window = Window.GetWindow(_button);
                if (_window != null)
                    _window.Deactivated += OnWindowDeactivated;

                var press = GetPressCommand(_button);
                var parameter = GetCommandParameter(_button);
                if (press != null && press.CanExecute(parameter))
                    press.Execute(parameter);
                return true;
            }

            /// <summary>Ends the active press, if any: the release runs once, and the capture and window hooks are dropped first.</summary>
            private void EndPress()
            {
                if (_source == PressSource.None)
                    return;

                var source = _source;
                var touch = _touch;
                _source = PressSource.None;
                _touch = null;

                if (_window != null)
                {
                    _window.Deactivated -= OnWindowDeactivated;
                    _window = null;
                }

                // Re-entrant LostMouseCapture / LostTouchCapture from these releases find no active press and do nothing.
                if (source == PressSource.Mouse && _button.IsMouseCaptured)
                    _button.ReleaseMouseCapture();
                if (source == PressSource.Touch && touch != null)
                    _button.ReleaseTouchCapture(touch);

                // The release is the Stop path, so it runs whenever set, even if CanExecute now reports false.
                var release = GetReleaseCommand(_button);
                release?.Execute(GetCommandParameter(_button));
            }
        }
    }
}
