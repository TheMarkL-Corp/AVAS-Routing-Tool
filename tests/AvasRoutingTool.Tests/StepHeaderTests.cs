using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Controls;
using Xunit;

namespace AvasRoutingTool.Tests
{
    [Collection("WpfSta")]
    public class StepHeaderTests
    {
        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try { TestResources.Ensure(); body(); }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("STA failure: " + failure);
        }

        private static StepHeader Header(int number, bool done)
        {
            var h = new StepHeader { Number = number, IsDone = done, Title = "PLACE SOURCES" };
            h.SetResourceReference(FrameworkElement.StyleProperty, "Style.StepHeader");
            return h;
        }

        private static void Showing(FrameworkElement el, Action assert)
        {
            var window = new Window { Content = el, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000 };
            window.Show();
            try { el.UpdateLayout(); assert(); }
            finally { window.Close(); }
        }

        private static List<T> All<T>(DependencyObject root) where T : DependencyObject
        {
            var found = new List<T>();
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) found.Add(match);
                found.AddRange(All<T>(child));
            }
            return found;
        }

        [Fact]
        public void StepHeader_IsNotAFocusStop() => RunSta(() =>
        {
            var h = new StepHeader();
            Assert.False(h.Focusable);
            Assert.False(h.IsTabStop);
        });

        private static Rect BoundsIn(FrameworkElement el, Visual root) =>
            el.TransformToAncestor(root).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));

        private static double Linear(byte c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        private static double Lum(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

        private static double Contrast(Color a, Color b)
        {
            var la = Lum(a);
            var lb = Lum(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        private static FrameworkElement Part(StepHeader h, string name) => (FrameworkElement)h.Template.FindName(name, h);

        [Fact]
        public void StepHeader_NotDone_ShowsNumberOnly() => RunSta(() =>
        {
            var h = Header(2, false);
            Showing(h, () =>
            {
                var number = (TextBlock)Part(h, "StepNumberText");
                Assert.Equal("2", number.Text);
                Assert.True(number.IsVisible);
                Assert.False(Part(h, "DoneCheck").IsVisible);
                Assert.Equal("PLACE SOURCES", ((TextBlock)Part(h, "LabelText")).Text);
                Assert.True(Part(h, "LabelText").IsVisible);
                var surface = (SolidColorBrush)Application.Current.FindResource("Brush.Surface.Elevated");
                Assert.Equal(surface.Color, ((SolidColorBrush)((Border)Part(h, "Marker")).Background).Color);
            });
        });

        [Fact]
        public void StepHeader_Done_KeepsTheNumber_AndShowsTheCheckAfterTheLabel() => RunSta(() =>
        {
            var h = Header(2, true);
            Showing(h, () =>
            {
                var number = Part(h, "StepNumberText");
                var label = Part(h, "LabelText");
                var check = (MaterialIcon)Part(h, "DoneCheck");
                Assert.True(number.IsVisible);
                Assert.True(check.IsVisible);
                Assert.Equal("check", check.Kind);
                var size = (double)Application.Current.FindResource("Size.Icon.S");
                Assert.Equal(size, check.ActualWidth, 1);
                Assert.Equal(size, check.ActualHeight, 1);
                var accent = (SolidColorBrush)Application.Current.FindResource("Brush.Text.Accent");
                Assert.Equal(accent.Color, ((SolidColorBrush)check.Foreground).Color);
                var gap = BoundsIn(check, h).Left - BoundsIn(label, h).Right;
                Assert.True(gap >= (double)Application.Current.FindResource("Size.Space.Action") - 0.5, "gap was " + gap);
                Assert.Empty(All<Wpf.Ui.Controls.SymbolIcon>(h));
            });
        });

        [Fact]
        public void StepHeader_Done_FillsTheMarker_WithReadableNumber() => RunSta(() =>
        {
            var themes = new AvasRoutingTool.Services.ThemeService();
            try
            {
                foreach (var mode in new[] { AvasRoutingTool.Services.ThemeMode.Dark, AvasRoutingTool.Services.ThemeMode.Light })
                {
                    themes.SetTheme(mode);
                    var h = Header(3, true);
                    Showing(h, () =>
                    {
                        var take = (SolidColorBrush)Application.Current.FindResource("Brush.Action.Take");
                        var onAction = (SolidColorBrush)Application.Current.FindResource("Brush.Text.OnAction");
                        var marker = (Border)Part(h, "Marker");
                        var number = (TextBlock)Part(h, "StepNumberText");
                        Assert.Equal(take.Color, ((SolidColorBrush)marker.Background).Color);
                        Assert.Equal(onAction.Color, ((SolidColorBrush)number.Foreground).Color);
                        var ratio = Contrast(((SolidColorBrush)number.Foreground).Color, ((SolidColorBrush)marker.Background).Color);
                        Assert.True(ratio >= 4.5, mode + " contrast " + ratio);
                    });
                }
            }
            finally { themes.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark); }
        });

        [Fact]
        public void StepHeader_Done_AnnouncementUnchanged() => RunSta(() =>
        {
            var h = Header(3, true);
            Showing(h, () =>
            {
                var peer = UIElementAutomationPeer.CreatePeerForElement(h);
                Assert.Equal(AutomationControlType.Text, peer.GetAutomationControlType());
                Assert.Equal("Level2", System.Windows.Automation.AutomationProperties.GetHeadingLevel(h).ToString());
                Assert.Contains("PLACE SOURCES", peer.GetName());
            });
        });

        [Fact]
        public void StepHeader_MarkerIsTheStepMarkerToken_AndIsAnnouncedAsText() => RunSta(() =>
        {
            var h = Header(1, false);
            Showing(h, () =>
            {
                var marker = (Border)h.Template.FindName("Marker", h);
                Assert.Equal((double)Application.Current.FindResource("Size.Composer.StepMarker"), marker.ActualHeight, 1);
                Assert.Equal(32, marker.ActualWidth, 1);
                var peer = UIElementAutomationPeer.CreatePeerForElement(h);
                Assert.Equal(AutomationControlType.Text, peer.GetAutomationControlType());
                Assert.Equal("Level2", System.Windows.Automation.AutomationProperties.GetHeadingLevel(h).ToString());
            });
        });
    }
}
