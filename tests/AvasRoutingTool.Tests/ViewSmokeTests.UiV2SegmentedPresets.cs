using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Services;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// UI v2 layout control on the display card's action bar (plan task A10, A13; round 3): More layouts and Copy layout are two ghost buttons of
    /// one style (Style.Button.ActionBar); the one-tap presets were removed (A13).
    /// </summary>
    public partial class ViewSmokeTests
    {
        private static Color PaletteColor(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;

        private static Color ColorOf(Brush? brush) => ((SolidColorBrush)brush!).Color;

        /// <summary>True when <paramref name="el"/> is <paramref name="ancestor"/> or lies inside it in the visual tree.</summary>
        private static bool Within(DependencyObject el, DependencyObject ancestor)
        {
            for (DependencyObject? d = el; d != null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            {
                if (d == ancestor) return true;
            }
            return false;
        }

        /// <summary>
        /// The topmost element a press at <paramref name="point"/> (in <paramref name="view"/>'s coordinates) reaches, as pointer input
        /// finds it: elements that are not hit-test visible (the group's outline) or not shown are skipped with their children.
        /// </summary>
        private static DependencyObject? PressTarget(Visual view, Point point)
        {
            DependencyObject? target = null;
            System.Windows.Media.VisualTreeHelper.HitTest(view,
                d => d is UIElement u && (!u.IsHitTestVisible || !u.IsVisible)
                    ? HitTestFilterBehavior.ContinueSkipSelfAndChildren
                    : HitTestFilterBehavior.Continue,
                r => { target = ((PointHitTestResult)r).VisualHit; return HitTestResultBehavior.Stop; },
                new PointHitTestParameters(point));
            return target;
        }

        [Fact]
        public void LayoutButton_TakesAPressOnItsWholeArea_IncludingTheEdges() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    // A press anywhere in the More layouts button must land on it, not only on its icon or label.
                    var segment = (Button)Named(view, "LayoutButton").Single();
                    double w = segment.ActualWidth, h = segment.ActualHeight;
                    var spots = new[]
                    {
                        new Point(1, 1), new Point(w - 1, 1), new Point(1, h - 1), new Point(w - 1, h - 1),
                        new Point(w / 2, 1), new Point(1, h / 2), new Point(w / 2, h / 2),
                    };
                    foreach (var spot in spots)
                    {
                        var atView = segment.TranslatePoint(spot, view);
                        var hit = PressTarget(view, atView);
                        string landsOn = hit == null ? "nothing" : $"{hit.GetType().Name} '{(hit as FrameworkElement)?.Name}' at {(hit is FrameworkElement hf ? BoundsIn(hf, view).ToString() : "")} (segment at {BoundsIn(segment, view)})";
                        Assert.True(hit != null && Within(hit, segment),
                            $"Layout {w:F0}x{h:F0}: a press at ({spot.X:F1},{spot.Y:F1}) lands on {landsOn}, not on the segment");
                    }
                });
            });

        [Fact]
        public void LayoutButton_ShowsIconLabelAndChevron_WithTheMoreLayoutsTooltipAndName() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    var layout = (Button)Named(view, "LayoutButton").Single();
                    Assert.Equal("More layouts", Assert.Single(FindAll<TextBlock>(layout).Where(t => !string.IsNullOrEmpty(t.Text))).Text);
                    var icons = FindAll<AvasRoutingTool.Controls.MaterialIcon>(layout);
                    Assert.Contains(icons, i => i.Kind == "grid_view");
                    Assert.Contains(icons, i => i.Kind == "expand_more");
                    Assert.Equal("More layouts", layout.ToolTip as string);
                    Assert.Equal("More layouts for RX1_c059e_SFPA", System.Windows.Automation.AutomationProperties.GetName(layout));
                });

                // At the carousel's card width Layout is still a touch target.
                var narrow = new CopyRig();
                var narrowView = narrow.View(narrow.Rx1, out var narrowHost, 400);
                Showing(narrowHost, () =>
                {
                    var layout = (Button)Named(narrowView, "LayoutButton").Single();
                    Assert.True(layout.IsVisible && layout.ActualWidth >= Token("Size.Touch.Min") && layout.ActualHeight >= Token("Size.Touch.Min"),
                        $"Layout is {layout.ActualWidth}x{layout.ActualHeight} at the carousel width");
                });
            });

        [Fact]
        public void CopyLayout_IsAnActionBarGhostButton_WithIconAndLabelSideBySide() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx1 = rig.View(rig.Rx1, out var h1);
                var rx3 = rig.View(rig.Rx3, out var h3);
                Showing(new StackPanel { Children = { h1, h3 } }, () =>
                {
                    var handle = (Button)Named(rx1, "CopyLayoutHandle").Single();
                    // The action-bar style, with Copy layout's own armed and nothing-to-copy states on top of it.
                    Assert.Same(Application.Current.FindResource("Style.Button.ActionBar"), handle.Style.BasedOn);
                    Assert.Same(Application.Current.FindResource("Style.Button.ActionBar.Copy"), handle.Style);
                    Assert.Equal(Token("Size.Touch.Min"), handle.MinHeight);
                    Assert.True(handle.ActualHeight >= Token("Size.Touch.Min"), $"Copy layout is {handle.ActualHeight} high");

                    var label = FindAll<TextBlock>(handle).Single(t => t.Text == "Copy layout");
                    var icon = (AvasRoutingTool.Controls.MaterialIcon)Named(rx1, "CopyIcon").Single();
                    Assert.Equal("content_copy", icon.Kind);
                    var iconBounds = BoundsIn(icon, handle);
                    var labelBounds = BoundsIn(label, handle);
                    Assert.True(iconBounds.Right <= labelBounds.Left + 0.5, "the icon is not before the label");
                    Assert.True(iconBounds.Top < labelBounds.Bottom && labelBounds.Top < iconBounds.Bottom, "the icon and the label are not in one row");
                    Assert.True(label.FontSize >= 12, $"the label is {label.FontSize} px");

                    Assert.Equal("Copy this layout to another display", handle.ToolTip as string);
                    var blocked = (Button)Named(rx3, "CopyLayoutHandle").Single();
                    Assert.Equal("No layout to copy yet", blocked.ToolTip as string);
                    Assert.True(blocked.ActualHeight >= Token("Size.Touch.Min"));
                });
            });

        [Fact]
        public void CopyLayout_WhileItIsTheCopySource_IsTheDropTargetColour() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx1 = rig.View(rig.Rx1, out var host);
                Showing(host, () =>
                {
                    var handle = (Button)Named(rx1, "CopyLayoutHandle").Single();
                    Assert.NotEqual(PaletteColor("Brush.Border.DropTarget"), ColorOf(handle.Background));
                    Click(handle);
                    Until(() => rig.Rx1.IsCopySource, "a tap did not arm RX1");
                    Assert.Equal(PaletteColor("Brush.Border.DropTarget"), ColorOf(handle.Background));
                });
            });

        [Fact]
        public void CopyLayout_WhenThereIsNothingToCopy_IsEnabledAndFocusable_ButLooksOff() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx3 = rig.View(rig.Rx3, out var host);
                Showing(host, () =>
                {
                    var handle = (Button)Named(rx3, "CopyLayoutHandle").Single();
                    Assert.False(rig.Rx3.CanCopyFrom);
                    Assert.True(handle.IsEnabled, "the handle is disabled: it cannot take keyboard focus or a tap");
                    Assert.True(handle.Focusable && handle.IsTabStop, "the handle is not a tab stop");
                    Assert.True(handle.ActualWidth >= 48 && handle.ActualHeight >= 48, $"the handle is {handle.ActualWidth}x{handle.ActualHeight}");

                    // Off is drawn by the style: the whole button is dimmed, with the arrow cursor.
                    Assert.Equal(0.5, handle.Opacity);
                    Assert.Equal(System.Windows.Input.Cursors.Arrow, handle.Cursor);
                });
            });

        [Fact]
        public void CopyLayout_PressedWithNothingToCopy_SaysWhyNextToTheButton_AndNothingArms() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx3 = rig.View(rig.Rx3, out var host);
                Showing(host, () =>
                {
                    var handle = (Button)Named(rx3, "CopyLayoutHandle").Single();
                    Click(handle);

                    var tip = rx3.CopyReasonTip;
                    Assert.NotNull(tip);
                    Assert.True(tip!.IsOpen, "pressing Copy layout with nothing to copy shows no reason");
                    Assert.Same(handle, tip.PlacementTarget);
                    Assert.Equal(rig.Rx3.CopyFromBlockedText, ((TextBlock)tip.Content).Text);
                    Assert.Null(rig.Vm.ArmedDisplay);
                    Assert.False(rig.Rx3.IsCopySource);
                });
            });

        [Fact]
        public void CopyLayout_TheReasonCloses_AfterItsTime() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx3 = rig.View(rig.Rx3, out var host);
                Showing(host, () =>
                {
                    rx3.CopyReasonShownFor = TimeSpan.FromMilliseconds(50);
                    var handle = (Button)Named(rx3, "CopyLayoutHandle").Single();
                    Click(handle);
                    Assert.True(rx3.CopyReasonTip?.IsOpen == true, "the reason did not open");
                    Until(() => rx3.CopyReasonTip?.IsOpen != true, "the reason did not close after its time");
                });
            });

        [Fact]
        public void CopyLayout_WhenThereIsSomethingToCopy_ArmsAndShowsNoReason() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx1 = rig.View(rig.Rx1, out var host);
                Showing(host, () =>
                {
                    var handle = (Button)Named(rx1, "CopyLayoutHandle").Single();
                    Click(handle);
                    Until(() => rig.Vm.ArmedDisplay == rig.Rx1, "a press did not arm RX1");
                    Assert.True(rx1.CopyReasonTip == null || !rx1.CopyReasonTip.IsOpen, "an armed copy shows a reason");
                    Click(handle); // stop again
                    Until(() => rig.Vm.ArmedDisplay == null, "a second press did not stop");
                });
            });

        [Fact]
        public void CopyLayout_IdleDisplays_ShowNoExtraText_UntilPressed() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx1 = rig.View(rig.Rx1, out var h1);
                var rx3 = rig.View(rig.Rx3, out var h3);
                Showing(new StackPanel { Children = { h1, h3 } }, () =>
                {
                    Assert.Null(rx1.CopyReasonTip);
                    Assert.Null(rx3.CopyReasonTip);
                    Assert.Empty(VisibleTexts(rx1, "RX1_c059e_SFPA shows nothing to copy"));
                    Assert.Empty(VisibleTexts(rx3, "RX3_1080P_SFPA shows nothing to copy"));
                });
            });

        /// <summary>The text colour and the fill the Copy layout button draws, as they resolved: the label's effective Foreground and part bd's Background.</summary>
        private static (Color Text, Color Fill) CopyLayoutPaint(Button handle)
        {
            var label = FindAll<TextBlock>(handle).Single(t => t.Text == "Copy layout");
            var bd = (System.Windows.Controls.Border)handle.Template.FindName("bd", handle);
            return (ColorOf(label.Foreground), ColorOf(bd.Background));
        }

        /// <summary>Forces IsMouseOver on a control: the input system owns that property, so the test sets its read-only key.</summary>
        private static void SetMouseOver(UIElement element, bool over)
        {
            var key = (DependencyPropertyKey)(typeof(UIElement)
                .GetField("IsMouseOverPropertyKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?
                .GetValue(null) ?? throw new InvalidOperationException("UIElement has no IsMouseOverPropertyKey field"));
            element.SetValue(key, over);
        }

        [Theory]
        [InlineData(ThemeMode.Dark)]
        [InlineData(ThemeMode.Light)]
        public void CopyLayout_WhileArmed_KeepsItsTextReadable_AtRestAndUnderTheMouse_InBothThemes(ThemeMode mode) =>
            RunSta(() =>
            {
                var theme = new ThemeService();
                try
                {
                    // The rig first: its MainViewModel builds a ThemeService that re-applies the saved (Dark) theme on construction.
                    var rig = new CopyRig();
                    theme.SetTheme(mode);
                    Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
                    var view = rig.View(rig.Rx1, out var host);
                    Showing(host, () =>
                    {
                        var handle = (Button)Named(view, "CopyLayoutHandle").Single();
                        Assert.True(rig.Rx1.IsCopySource, "RX1 is not armed");
                        var readings = new List<string>();
                        bool readable = true;
                        foreach (var hovered in new[] { false, true })
                        {
                            SetMouseOver(handle, hovered);
                            Idle();
                            var (text, fill) = CopyLayoutPaint(handle);
                            double ratio = Contrast(text, fill);
                            readable &= ratio >= 4.5;
                            readings.Add($"{(hovered ? "under the mouse" : "at rest")}: text {text} on fill {fill} = {ratio:F2}:1");
                        }
                        SetMouseOver(handle, false);
                        Assert.True(readable, $"{mode}, armed (need 4.5:1): " + string.Join("; ", readings));
                    });
                }
                finally { theme.SetTheme(ThemeMode.Dark); }
            });

        [Fact]
        public void DisplayCard_Texts_SayNoSourceAndPickASource() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx3, out var host, 613); // unrouted
                Showing(host, () =>
                {
                    Assert.Equal("No source", ((TextBlock)Named(view, "ModeBadgeText").Single()).Text);
                    Assert.Equal("Pick a source above, then tap here or drag it in.", ((TextBlock)Named(view, "ReadyToRouteHint").Single()).Text);
                });
            });
    }
}
