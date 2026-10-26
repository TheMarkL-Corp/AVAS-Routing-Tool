using System.Windows;
using System.Windows.Input;

namespace AvasRoutingTool.Views
{
    /// <summary>
    /// Keyboard or pointer: what the last input was. Focus rings are for keyboard users only - on a touch-first tool a
    /// finger press that focuses a button must not leave a ring on it. A control draws its ring while
    /// <see cref="ShowsFocusProperty"/> is true: it has keyboard focus <i>and</i> the last input was the keyboard.
    /// Border-based rings (not adorners) bind to this, so they also render into a bitmap.
    /// </summary>
    public static class InputModality
    {
        /// <summary>The element has keyboard focus and the last input was the keyboard (set here, read by triggers).</summary>
        public static readonly DependencyProperty ShowsFocusProperty = DependencyProperty.RegisterAttached(
            "ShowsFocus", typeof(bool), typeof(InputModality), new PropertyMetadata(false));

        public static bool GetShowsFocus(DependencyObject element) => (bool)element.GetValue(ShowsFocusProperty);

        private static void SetShowsFocus(DependencyObject element, bool value) => element.SetValue(ShowsFocusProperty, value);

        /// <summary>The last input was a key press (true), or a mouse, touch or pen press (false, the start state). Tests set it.</summary>
        public static bool IsKeyboard { get; set; }

        static InputModality()
        {
            // Class handlers on every UIElement, also for handled events: any key press, then any pointer press, decides.
            EventManager.RegisterClassHandler(typeof(UIElement), Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, _) => Switch(true)), true);
            EventManager.RegisterClassHandler(typeof(UIElement), Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => Switch(false)), true);
            EventManager.RegisterClassHandler(typeof(UIElement), UIElement.PreviewTouchDownEvent, new System.EventHandler<TouchEventArgs>((_, _) => Switch(false)), true);
            EventManager.RegisterClassHandler(typeof(UIElement), Stylus.PreviewStylusDownEvent, new StylusDownEventHandler((_, _) => Switch(false)), true);
            EventManager.RegisterClassHandler(typeof(UIElement), Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotFocus), true);
            EventManager.RegisterClassHandler(typeof(UIElement), Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnLostFocus), true);
        }

        /// <summary>Makes sure the input hooks exist (touching the type runs its static constructor); <c>App.OnStartup</c> calls it before the first window is built.</summary>
        public static void Initialize()
        {
        }

        private static void Switch(bool keyboard)
        {
            if (IsKeyboard == keyboard) return;
            IsKeyboard = keyboard;
            // The element that has focus now: its ring appears with the first key, and goes with the first press.
            if (Keyboard.FocusedElement is DependencyObject focused) SetShowsFocus(focused, keyboard);
        }

        private static void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (ReferenceEquals(sender, e.NewFocus) && sender is DependencyObject element) SetShowsFocus(element, IsKeyboard);
        }

        private static void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (ReferenceEquals(sender, e.OldFocus) && sender is DependencyObject element) SetShowsFocus(element, false);
        }
    }
}
