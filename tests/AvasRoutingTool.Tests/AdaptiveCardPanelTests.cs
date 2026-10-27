using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Controls;
using Xunit;

namespace AvasRoutingTool.Tests
{
    [Collection("WpfSta")]
    public class AdaptiveCardPanelTests
    {
        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("STA failure: " + failure);
        }

        [Fact]
        public void MeasureAndArrange_TwoChildrenOnFullHd_ArrangesWithComputedCardDimensions()
        {
            RunSta(() =>
            {
                var panel = new AdaptiveCardPanel();
                var child1 = new Border();
                var child2 = new Border();
                panel.Children.Add(child1);
                panel.Children.Add(child2);

                var availableSize = new Size(1888, 435);
                panel.Measure(availableSize);

                var expectedPlan = AdaptiveGridLayout.Compute(2, 1888, 435, GridSpec.Default);

                Assert.True(panel.DesiredSize.Width > 0);
                Assert.True(panel.DesiredSize.Height > 0);

                panel.Arrange(new Rect(0, 0, 1888, 435));

                Assert.Equal(expectedPlan.CardWidth, child1.RenderSize.Width, 2);
                Assert.Equal(expectedPlan.CardHeight, child1.RenderSize.Height, 2);
                Assert.Equal(expectedPlan.CardWidth, child2.RenderSize.Width, 2);
                Assert.Equal(expectedPlan.CardHeight, child2.RenderSize.Height, 2);

                // Check that children are placed symmetrically (centered horizontally)
                var slot1 = child1.TransformToAncestor(panel).Transform(new Point(0, 0));
                var slot2 = child2.TransformToAncestor(panel).Transform(new Point(0, 0));
                double leftMargin = slot1.X;
                double rightMargin = 1888 - (slot2.X + child2.RenderSize.Width);
                Assert.Equal(leftMargin, rightMargin, 2);
            });
        }

        [Fact]
        public void MeasureAndArrange_InsideScrollViewer_SizesCardsToFitViewport()
        {
            RunSta(() =>
            {
                var scrollViewer = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Width = 1888,
                    Height = 435
                };
                var itemsControl = new ItemsControl
                {
                    ItemsSource = new[] { "Card1", "Card2" }
                };
                var template = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(AdaptiveCardPanel)));
                itemsControl.ItemsPanel = template;

                scrollViewer.Content = itemsControl;

                var window = new Window
                {
                    Content = scrollViewer,
                    Width = 1920,
                    Height = 1080,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Left = -10000
                };
                window.Show();
                scrollViewer.UpdateLayout();

                var panel = FindVisualChild<AdaptiveCardPanel>(itemsControl);
                Assert.NotNull(panel);

                var expectedPlan = AdaptiveGridLayout.Compute(2, 1888, 435, GridSpec.Default);
                var container1 = (FrameworkElement)itemsControl.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.NotNull(container1);
                Assert.Equal(expectedPlan.CardWidth, container1.ActualWidth, 1);
                Assert.Equal(expectedPlan.CardHeight, container1.ActualHeight, 1);

                window.Close();
            });
        }

        [Fact]
        public void MeasureAndArrange_EightCards_InsideScrollViewer_EnablesScrollingAtMinWidth()
        {
            RunSta(() =>
            {
                var scrollViewer = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Width = 1888,
                    Height = 435
                };
                var itemsControl = new ItemsControl
                {
                    ItemsSource = new[] { "1", "2", "3", "4", "5", "6", "7", "8" }
                };
                var template = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(AdaptiveCardPanel)));
                itemsControl.ItemsPanel = template;

                scrollViewer.Content = itemsControl;

                var window = new Window
                {
                    Content = scrollViewer,
                    Width = 1920,
                    Height = 1080,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Left = -10000
                };
                window.Show();
                scrollViewer.UpdateLayout();

                var expectedPlan = AdaptiveGridLayout.Compute(8, 1888, 435, GridSpec.Default);
                Assert.True(expectedPlan.NeedsScroll);

                var container1 = (FrameworkElement)itemsControl.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.NotNull(container1);
                Assert.Equal(GridSpec.Default.MinCardWidth, container1.ActualWidth, 1);
                Assert.True(scrollViewer.ScrollableHeight > 0);

                window.Close();
            });
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var nested = FindVisualChild<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}
