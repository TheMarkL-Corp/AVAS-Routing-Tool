using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AvasRoutingTool.Controls;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class FractionalLayoutPanelTests
    {
        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("STA failure: " + failure);
        }

        private static Border Child(double x, double y, double w, double h)
        {
            var b = new Border();
            FractionalLayoutPanel.SetX(b, x);
            FractionalLayoutPanel.SetY(b, y);
            FractionalLayoutPanel.SetW(b, w);
            FractionalLayoutPanel.SetH(b, h);
            return b;
        }

        private static void Layout(Panel panel, double w, double h)
        {
            panel.Measure(new Size(w, h));
            panel.Arrange(new Rect(0, 0, w, h));
        }

        [Fact]
        public void Children_AreArrangedAtFractionsOfTheAvailableSpace() => RunSta(() =>
        {
            var panel = new FractionalLayoutPanel();
            var quarter = Child(0.5, 0.5, 0.5, 0.5);
            panel.Children.Add(quarter);

            Layout(panel, 800, 450);

            Assert.Equal(new Rect(400, 225, 400, 225), LayoutInformation.GetLayoutSlot(quarter));
            Assert.Equal(400, quarter.ActualWidth, 1);
            Assert.Equal(225, quarter.ActualHeight, 1);
        });

        [Fact]
        public void LaterChildren_AreAddedLastSoTheyPaintOnTop() => RunSta(() =>
        {
            var panel = new FractionalLayoutPanel();
            var back = Child(0, 0, 1, 1);
            var front = Child(0.7, 0.7, 0.25, 0.25);
            panel.Children.Add(back);
            panel.Children.Add(front);

            Layout(panel, 800, 450);

            Assert.True(panel.Children.IndexOf(front) > panel.Children.IndexOf(back));
            Assert.Equal(200, front.ActualWidth, 1);
        });

        [Fact]
        public void AspectRatio_LetterboxesTheCanvasInsideWiderSpace() => RunSta(() =>
        {
            var panel = new FractionalLayoutPanel { AspectRatio = 16.0 / 9.0 };
            var full = Child(0, 0, 1, 1);
            panel.Children.Add(full);

            Layout(panel, 1600, 450); // much wider than 16:9 -> height-limited: 800x450 centred

            var slot = LayoutInformation.GetLayoutSlot(full);
            Assert.Equal(800, slot.Width, 1);
            Assert.Equal(450, slot.Height, 1);
            Assert.Equal(400, slot.X, 1);
            Assert.Equal(0, slot.Y, 1);
        });

        [Fact]
        public void AspectRatio_LetterboxesTheCanvasInsideTallerSpace() => RunSta(() =>
        {
            var panel = new FractionalLayoutPanel { AspectRatio = 16.0 / 9.0 };
            var full = Child(0, 0, 1, 1);
            panel.Children.Add(full);

            Layout(panel, 800, 900); // taller than 16:9 -> width-limited: 800x450 centred vertically

            var slot = LayoutInformation.GetLayoutSlot(full);
            Assert.Equal(800, slot.Width, 1);
            Assert.Equal(450, slot.Height, 1);
            Assert.Equal(225, slot.Y, 1);
        });

        [Fact]
        public void NoAspectRatio_UsesTheWholeSpace() => RunSta(() =>
        {
            var panel = new FractionalLayoutPanel();
            var full = Child(0, 0, 1, 1);
            panel.Children.Add(full);

            Layout(panel, 1600, 450);

            Assert.Equal(1600, full.ActualWidth, 1);
            Assert.Equal(450, full.ActualHeight, 1);
        });

        [Fact]
        public void UnboundedMeasure_DoesNotThrowAndReportsZero() => RunSta(() =>
        {
            var panel = new FractionalLayoutPanel();
            panel.Children.Add(Child(0, 0, 1, 1));

            panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Assert.Equal(0, panel.DesiredSize.Width);
            Assert.Equal(0, panel.DesiredSize.Height);
        });
    }
}
