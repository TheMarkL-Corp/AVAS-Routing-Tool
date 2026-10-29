using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Controls;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
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

        [Fact]
        public void StepHeader_NotDone_ShowsNumber_Done_ShowsCheckSymbol() => RunSta(() =>
        {
            var h = Header(2, false);
            Showing(h, () =>
            {
                var number = All<TextBlock>(h).Find(t => t.Text == "2");
                Assert.NotNull(number);
                Assert.True(number!.IsVisible);
                Assert.DoesNotContain(All<SymbolIcon>(h), i => i.IsVisible);
                Assert.Contains(All<TextBlock>(h), t => t.Text == "PLACE SOURCES" && t.IsVisible);

                h.IsDone = true;
                h.UpdateLayout();

                var icon = Assert.Single(All<SymbolIcon>(h), i => i.IsVisible);
                Assert.Equal(SymbolRegular.Checkmark24, icon.Symbol);
                Assert.False(number.IsVisible);
                var marker = (Border)h.Template.FindName("Marker", h);
                var take = (SolidColorBrush)Application.Current.FindResource("Brush.Action.Take");
                Assert.Equal(take.Color, ((SolidColorBrush)marker.Background).Color);
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
