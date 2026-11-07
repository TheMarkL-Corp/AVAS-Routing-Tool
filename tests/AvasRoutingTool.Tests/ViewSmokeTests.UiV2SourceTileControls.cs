using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Views;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>Round 3, Main Window source tiles: Start, Stop and Refresh are one ActionBar-family outlined button (48 px), and the standby placeholder text is readable.</summary>
    public partial class ViewSmokeTests
    {
        [Fact]
        public void SourceTile_StartStopRefresh_AreOneOutlinedActionBarButton_48High() =>
            RunSta(() =>
            {
                var vm = UiV2Source();
                vm.IsStreaming = true;
                SourceCardAt(vm, view => AssertTileButtons(view, "RefreshStreamButton", "StopStreamButton"));
                vm.IsStreaming = false;
                SourceCardAt(vm, view => AssertTileButtons(view, "StartStreamButton"));
            });

        private static void AssertTileButtons(SourceCardView view, params string[] names)
        {
            var outlined = Application.Current.FindResource("Style.Button.ActionBar.Outlined");
            foreach (var name in names)
            {
                var b = (Button)Named(view, name).Single();
                Assert.True(b.IsVisible, name);
                Assert.Same(outlined, b.Style);
                Assert.Equal(Token("Size.Touch.Min"), b.ActualHeight);
            }
        }

        private static double RelLuminance(Color c)
        {
            static double L(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * L(c.R) + 0.7152 * L(c.G) + 0.0722 * L(c.B);
        }

        [Fact]
        public void SourceTile_StandbyPlaceholderText_IsNotDimmed_AndReadsAt4_5To1() =>
            RunSta(() =>
                SourceCardAt(UiV2Source(), view =>
                {
                    var caption = FindAll<TextBlock>(view).First(t => t.IsVisible && t.Text == "Preview Standby");
                    double opacity = 1;
                    for (DependencyObject d = caption; d != null && d != view; d = VisualTreeHelper.GetParent(d))
                        if (d is UIElement e) opacity *= e.Opacity;
                    Assert.True(opacity >= 0.99, $"the standby text is drawn at opacity {opacity:F2}");
                    var fg = ((SolidColorBrush)caption.Foreground).Color;
                    var bg = ((SolidColorBrush)Application.Current.FindResource("Brush.Surface.PreviewTile")).Color;
                    double l1 = RelLuminance(fg), l2 = RelLuminance(bg);
                    double ratio = (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
                    Assert.True(ratio >= 4.5, $"contrast {ratio:F2}");
                }));
    }
}
