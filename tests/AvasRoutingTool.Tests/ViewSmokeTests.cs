using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Instantiates the real XAML views with real view models and forces a layout pass.
    /// Catches binding/resource crashes (e.g. TwoWay binding on a read-only property) that
    /// pure view-model tests can never see.
    /// </summary>
    public class ViewSmokeTests
    {
        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    foreach (var name in new[] { "Theme", "Icons" })
                    {
                        app.Resources.MergedDictionaries.Add(new ResourceDictionary
                        {
                            Source = new Uri($"pack://application:,,,/AvasRoutingTool;component/Resources/{name}.xaml")
                        });
                    }
                    body();
                }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("View smoke failure: " + failure);
        }

        private static void Layout(FrameworkElement el)
        {
            var window = new Window { Content = el, Width = 1920, Height = 1080, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000 };
            window.Show();
            el.Measure(new Size(1920, 1080));
            el.Arrange(new Rect(0, 0, 1920, 1080));
            el.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            window.Close();
        }

        private static AvasDevice Rx(string src = "") => new()
        {
            DeviceId = "74fe483c059e", DeviceName = "RX1", IsReceiver = true, IsActive = true,
            ActiveSourceId = src, ActiveSourceName = src == "" ? "" : "TX2"
        };

        [Fact]
        public void DisplayCardView_Unrouted_RendersWithoutBindingCrash() =>
            RunSta(() => Layout(new DisplayCardView { DataContext = new DisplayCardViewModel(Rx()) }));

        [Fact]
        public void DisplayCardView_Routed_RendersWithoutBindingCrash() =>
            RunSta(() => Layout(new DisplayCardView { DataContext = new DisplayCardViewModel(Rx("74fe488b07bb")) }));

        [Fact]
        public void SourceCardView_RendersWithoutBindingCrash() =>
            RunSta(() => Layout(new SourceCardView
            {
                DataContext = new SourceCardViewModel(new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = "TX2", VendorId = 105, IsTransmitter = true, IsActive = true })
            }));
    }
}
