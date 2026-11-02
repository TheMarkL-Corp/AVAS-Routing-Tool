using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AvasRoutingTool.Services.Visca;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Hold-to-move behaviour (RC10, R4): Press fires once when the button goes down, Release fires exactly once per press
    /// on every exit the plan names. A release without a press fires nothing.
    /// </summary>
    [Collection("WpfSta")]
    public class HoldButtonTests
    {
        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    TestResources.Ensure();
                    body();
                }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("Hold button failure: " + failure);
        }

        /// <summary>Counts Execute calls; CanExecute is true unless a test clears <see cref="CanRun"/>.</summary>
        private sealed class Recorder : ICommand
        {
            public int Executions { get; private set; }
            public object? LastParameter { get; private set; }
            public bool CanRun { get; set; } = true;

            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => CanRun;
            public void Execute(object? parameter)
            {
                Executions++;
                LastParameter = parameter;
            }
        }

        /// <summary>A Window whose Deactivated can be raised from a test (OnDeactivated is protected).</summary>
        private sealed class TestWindow : Window
        {
            public void RaiseDeactivated() => OnDeactivated(EventArgs.Empty);
        }

        private static (Button Button, Recorder Press, Recorder Release) Held()
        {
            var button = new Button();
            var press = new Recorder();
            var release = new Recorder();
            HoldButton.SetPressCommand(button, press);
            HoldButton.SetReleaseCommand(button, release);
            HoldButton.SetCommandParameter(button, PanTiltDirection.Left);
            return (button, press, release);
        }

        private static void MouseDown(UIElement element) =>
            element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
            });

        private static void MouseUp(UIElement element) =>
            element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
            });

        private static void LoseMouseCapture(UIElement element) =>
            element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.LostMouseCaptureEvent });

        [Fact]
        public void Press_Release_FiresOnceEach() =>
            RunSta(() =>
            {
                var (button, press, release) = Held();

                MouseDown(button);
                Assert.Equal(1, press.Executions);
                Assert.Equal(0, release.Executions);
                Assert.Equal(PanTiltDirection.Left, press.LastParameter);

                MouseUp(button);
                Assert.Equal(1, press.Executions);
                Assert.Equal(1, release.Executions);
                Assert.Equal(PanTiltDirection.Left, release.LastParameter);

                // Every later exit event for the same press is a no-op.
                LoseMouseCapture(button);
                MouseUp(button);
                Assert.Equal(1, press.Executions);
                Assert.Equal(1, release.Executions);
            });

        [Fact]
        public void PressThenLoseCapture_FiresRelease() =>
            RunSta(() =>
            {
                var (button, press, release) = Held();

                MouseDown(button);
                LoseMouseCapture(button);

                Assert.Equal(1, press.Executions);
                Assert.Equal(1, release.Executions);
            });

        [Fact]
        public void PressThenDisable_FiresRelease() =>
            RunSta(() =>
            {
                var (button, press, release) = Held();

                MouseDown(button);
                button.IsEnabled = false;

                Assert.Equal(1, press.Executions);
                Assert.Equal(1, release.Executions);
            });

        [Fact]
        public void PressThenUnload_FiresRelease() =>
            RunSta(() =>
            {
                var (button, press, release) = Held();

                MouseDown(button);
                button.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

                Assert.Equal(1, press.Executions);
                Assert.Equal(1, release.Executions);
            });

        [Fact]
        public void Release_RunsEvenWhenCommandCannotExecute() =>
            RunSta(() =>
            {
                var (button, press, release) = Held();

                MouseDown(button);
                release.CanRun = false; // e.g. the view model is locked after the press

                MouseUp(button);

                Assert.Equal(1, press.Executions);
                Assert.Equal(1, release.Executions);
                Assert.Equal(PanTiltDirection.Left, release.LastParameter);
            });

        [Fact]
        public void ReleaseWithoutPress_FiresNothing() =>
            RunSta(() =>
            {
                var (button, press, release) = Held();

                MouseUp(button);
                LoseMouseCapture(button);
                button.IsEnabled = false;
                button.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

                Assert.Equal(0, press.Executions);
                Assert.Equal(0, release.Executions);
            });

        [Fact]
        public void WindowDeactivated_FiresRelease() =>
            RunSta(() =>
            {
                var (button, press, release) = Held();
                var window = new TestWindow { Content = button, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000 };
                window.Show();
                try
                {
                    MouseDown(button);
                    Assert.Equal(1, press.Executions);
                    Assert.Equal(0, release.Executions);

                    window.RaiseDeactivated();
                    Assert.Equal(1, release.Executions);

                    MouseUp(button);
                    Assert.Equal(1, release.Executions);
                }
                finally
                {
                    window.Close();
                }
            });

        [Fact]
        public void ClearingTheAttachedProperties_DetachesEveryHandler() =>
            RunSta(() =>
            {
                var (button, press, release) = Held();
                HoldButton.SetPressCommand(button, null);
                HoldButton.SetReleaseCommand(button, null);

                MouseDown(button);
                MouseUp(button);
                LoseMouseCapture(button);

                Assert.Equal(0, press.Executions);
                Assert.Equal(0, release.Executions);
            });
    }
}
