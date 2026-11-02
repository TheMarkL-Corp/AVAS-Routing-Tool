using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Services;
using AvasRoutingTool.Tests.Fakes;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// The direction pad of the Room Cameras tab (RC10): nine keys with spoken names, each at least the 48 px touch minimum
    /// at the 1920x1080 and 1024x720 layouts, in both themes.
    /// </summary>
    [Collection("WpfSta")]
    public class UiV2PtzPadTests
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
            if (failure != null) throw new Xunit.Sdk.XunitException("Ptz pad failure: " + failure);
        }

        private static RoomCameraViewModel Camera() =>
            new(new RoomCameraConfig { Name = "ROOM-CAM", Host = "192.168.1.163", Port = 1259 }, new FakePtzCamera(), () => DateTime.UtcNow);

        private static List<Button> Keys(DependencyObject root)
        {
            var found = new List<Button>();
            Collect(root, found);
            return found;
        }

        private static void Collect(DependencyObject root, List<Button> found)
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is Button b) found.Add(b);
                Collect(child, found);
            }
        }

        /// <summary>Hosts the pad in a window of the given size, pad at the rail width of that layout, and runs the assertions while shown.</summary>
        private static void Hosted(PtzPadView pad, double width, double height, double railWidth, Action assert)
        {
            pad.Width = railWidth;
            pad.HorizontalAlignment = HorizontalAlignment.Left;
            var window = new Window { Content = pad, Width = width, Height = height, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000 };
            window.Show();
            try
            {
                window.UpdateLayout();
                assert();
            }
            finally
            {
                window.Close();
            }
        }

        [Fact]
        public void Pad_HasNineKeys_AllWithAutomationNames() =>
            RunSta(() =>
            {
                var pad = new PtzPadView { DataContext = Camera() };
                Hosted(pad, 1920, 1080, 360, () =>
                {
                    var keys = Keys(pad);
                    Assert.Equal(9, keys.Count);
                    Assert.All(keys, k => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(k)), "a pad key has no spoken name"));
                    Assert.Contains(keys, k => AutomationProperties.GetName(k) == "Pan left, hold to move");
                    Assert.Contains(keys, k => AutomationProperties.GetName(k) == "Home camera, press");
                });
            });

        [Fact]
        public void DirectionKeys_AreNotAllOneGlyph_AndDownRightIsNotTheBrokenArrow() =>
            RunSta(() =>
            {
                var pad = new PtzPadView { DataContext = Camera() };
                Hosted(pad, 1920, 1080, 360, () =>
                {
                    var keys = Keys(pad);
                    var directions = keys.Where(k => AutomationProperties.GetName(k) != "Home camera, press").ToList();
                    Assert.Equal(8, directions.Count);
                    var symbols = directions.Select(k => ((Wpf.Ui.Controls.SymbolIcon)k.Content).Symbol).ToList();
                    Assert.True(symbols.Distinct().Count() > 1, "all eight direction keys show the same glyph");

                    var downRight = keys.Single(k => AutomationProperties.GetName(k) == "Pan down-right, hold to move");
                    Assert.NotEqual(Wpf.Ui.Controls.SymbolRegular.ArrowDownRight24, ((Wpf.Ui.Controls.SymbolIcon)downRight.Content).Symbol);
                });
            });

        [Theory]
        [InlineData(1920, 1080, 360, false)]
        [InlineData(1920, 1080, 360, true)]
        [InlineData(1024, 720, 300, false)]
        [InlineData(1024, 720, 300, true)]
        public void Keys_AreAtLeast48px(double width, double height, double railWidth, bool light) =>
            RunSta(() =>
            {
                if (light) new AvasRoutingTool.Services.ThemeService().SetTheme(AvasRoutingTool.Services.ThemeMode.Light);
                try
                {
                    var minimum = (double)Application.Current.FindResource("Size.Touch.Min");
                    var pad = new PtzPadView { DataContext = Camera() };
                    Hosted(pad, width, height, railWidth, () =>
                    {
                        var keys = Keys(pad);
                        Assert.Equal(9, keys.Count);
                        foreach (var key in keys)
                        {
                            Assert.True(key.ActualWidth >= minimum, $"{AutomationProperties.GetName(key)} is {key.ActualWidth} px wide at {width}x{height}");
                            Assert.True(key.ActualHeight >= minimum, $"{AutomationProperties.GetName(key)} is {key.ActualHeight} px high at {width}x{height}");
                        }
                    });
                }
                finally
                {
                    if (light) new AvasRoutingTool.Services.ThemeService().SetTheme(AvasRoutingTool.Services.ThemeMode.Dark);
                }
            });
    }
}
