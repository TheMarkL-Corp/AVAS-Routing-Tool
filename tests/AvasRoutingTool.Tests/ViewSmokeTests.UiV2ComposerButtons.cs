using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AvasRoutingTool.Controls;
using AvasRoutingTool.Services;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Composer round, Task 2: the Composer's secondary buttons are one outlined action-bar standard (Style.Button.ActionBar.Outlined):
    /// Size.Action.PadX each side, Size.Touch.Min high (Size.Touch.Compact on the compact Replace/Move strip), a Material icon of
    /// Size.Icon.Layout + Size.Space.Action + a Font.Size.Body SemiBold label, and Size.Space.Action between neighbours.
    /// </summary>
    public partial class ViewSmokeTests
    {
        private static double ButtonToken(string key) => (double)Application.Current.FindResource(key);

        /// <summary>One button the Composer shows in a realistic state: its name, the button, and the icon it must carry.</summary>
        private sealed record ComposerButton(string Name, Button Button, string IconKind);

        /// <summary>
        /// Drives the Composer through the states that make every secondary button visible (a second Take so Undo is offered, a placed block
        /// selected so Replace/Move show, a source armed for Done placing, a block mid-move for Cancel move, the picker open) and hands each
        /// visible button to <paramref name="visit"/> with the layout current.
        /// </summary>
        private static void ForEachComposerButton(Action<ComposerButton> visit)
        {
            var rig = new ComposerRig();
            var c = rig.Composer("quad_1_3");
            c.Arm(rig.Tx1);
            c.TapBlock(0);
            c.Disarm();
            Pump(c.TakeAsync);
            c.Arm(rig.Tx2);
            c.TapBlock(1);
            c.Disarm();
            Pump(c.TakeAsync); // a second Take: there is now a previous picture to go back to
            Assert.True(c.CanUndo);
            c.Arm(rig.Tx1);
            c.TapBlock(2);     // staged edit: Discard and Clear all are enabled
            c.Disarm();

            var view = rig.View(c);
            Showing(view, () =>
            {
                void One(string name, string icon, DependencyObject root, Func<FrameworkElement, bool>? pick = null)
                {
                    var found = Named(root, name).Where(e => e.IsVisible && (pick == null || pick(e))).ToList();
                    Assert.True(found.Count == 1, $"{name}: expected one visible button, found {found.Count}");
                    var button = Assert.IsAssignableFrom<Button>(found[0]);
                    Assert.True(button.ActualHeight > 0 && button.ActualWidth > 0, $"{name} has no size");
                    visit(new ComposerButton(name, button, icon));
                }

                // state A: nothing armed, a placed block selected
                Relayout(view);
                c.TapBlock(0);
                Relayout(view);
                Assert.True(c.Blocks[0].IsSelected);
                One("CloseComposerButton", "arrow_back", view);
                One("UndoButton", "undo", view);
                One("DiscardButton", "close", view);
                One("ClearAllButton", "delete_sweep", view);
                One("ReplaceButton", "swap_horiz", view);
                One("MoveButton", "open_with", view);

                // state B: a source armed
                c.Arm(rig.Tx2);
                Relayout(view);
                One("DoneButton", "check", view);
                c.Disarm();

                // state C: a block mid-move
                c.TapBlock(0);
                c.BeginMove(0);
                Relayout(view);
                Assert.NotNull(c.MovingFromIndex);
                One("DoneButton", "close", view);
                c.Disarm();

                // state D: the picker open on an empty block
                Relayout(view);
                c.TapBlock(3);
                Relayout(view);
                var popup = (Popup)Named(view, "PickerPopup").Single();
                Assert.True(popup.IsOpen);
                Relayout((FrameworkElement)popup.Child);
                One("PickerCancelButton", "close", popup.Child);
            });
        }

        private static bool IsOutlined(Style? style)
        {
            var outlined = (Style)Application.Current.FindResource("Style.Button.ActionBar.Outlined");
            for (var s = style; s != null; s = s.BasedOn)
                if (ReferenceEquals(s, outlined)) return true;
            return false;
        }

        /// <summary>The root of the visual tree an element is in (the window content, or the popup's own root).</summary>
        private static Visual TopVisual(DependencyObject d)
        {
            while (VisualTreeHelper.GetParent(d) is { } parent) d = parent;
            return (Visual)d;
        }

        private static TextBlock LabelOfButton(Button b) => FindAll<TextBlock>(b).Single(t => t.IsVisible);

        [Fact]
        public void ComposerButtons_UseTheOutlinedActionBarStyle() =>
            RunSta(() =>
            {
                var seen = new List<string>();
                ForEachComposerButton(b =>
                {
                    seen.Add(b.Name);
                    Assert.True(IsOutlined(b.Button.Style), $"{b.Name} is not Style.Button.ActionBar.Outlined or based on it");
                    Assert.Equal(2d, b.Button.BorderThickness.Left);
                    Assert.Equal(ButtonToken("Size.Touch.Min"), b.Button.MinHeight);
                });
                Assert.Equal(
                    new[] { "ClearAllButton", "CloseComposerButton", "DiscardButton", "DoneButton", "MoveButton", "PickerCancelButton", "ReplaceButton", "UndoButton" },
                    seen.Distinct().OrderBy(n => n, StringComparer.Ordinal));
            });

        [Fact]
        public void ComposerButtons_HaveTheStandardPaddingAndHeight() =>
            RunSta(() =>
            {
                var padX = ButtonToken("Size.Action.PadX");
                var min = ButtonToken("Size.Touch.Min");
                ForEachComposerButton(b =>
                {
                    Assert.Equal(padX, b.Button.Padding.Left);
                    Assert.Equal(padX, b.Button.Padding.Right);
                    Assert.Equal(min, b.Button.ActualHeight, 0.5);
                });
            });

        [Fact]
        public void ComposerButtons_TheCompactStripIsSize_Touch_Compact_WithTheStandardPadding() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("pip");
                c.Arm(rig.Tx1);
                c.TapBlock(0);
                c.Disarm();
                c.TapBlock(0);
                var view = rig.View(c);
                ShowingAt(view, 1024, 720, () =>
                {
                    Assert.True(c.Blocks[0].IsSelected);
                    var compact = ButtonToken("Size.Touch.Compact");
                    var padX = ButtonToken("Size.Action.PadX");
                    foreach (var name in new[] { "ReplaceButton", "MoveButton" })
                    {
                        var b = (Button)Named(view, name).Single(e => e.IsVisible);
                        Assert.True(BlockDensity.GetIsCompact(b), $"{name} sits in a block that is not compact (pick a smaller block)");
                        Assert.Equal(compact, b.ActualHeight, 0.5);
                        Assert.Equal(padX, b.Padding.Left);
                        Assert.Equal(padX, b.Padding.Right);
                        Assert.True(IsOutlined(b.Style));
                        var icon = FindAll<MaterialIcon>(b).Single();
                        var label = LabelOfButton(b);
                        Assert.Equal(ButtonToken("Size.Space.Action"), BoundsIn(label, view).Left - BoundsIn(icon, view).Right, 0.5);
                    }
                });
            });

        [Fact]
        public void ComposerButtons_IconLabelGapIsSpaceAction() =>
            RunSta(() =>
            {
                var gap = ButtonToken("Size.Space.Action");
                var layout = ButtonToken("Size.Icon.Layout");
                ForEachComposerButton(b =>
                {
                    var icon = FindAll<MaterialIcon>(b.Button).Single();
                    Assert.Equal(b.IconKind, icon.Kind);
                    Assert.Equal(layout, icon.ActualWidth, 0.5);
                    Assert.Equal(layout, icon.ActualHeight, 0.5);
                    var label = LabelOfButton(b.Button);
                    // Undo's label wraps (and centres), so the layout box of the label is what sits Size.Space.Action from the icon
                    var root = TopVisual(b.Button);
                    Assert.Equal(gap, BoundsIn(label, root).Left - BoundsIn(icon, root).Right, 0.5);
                });
            });

        [Fact]
        public void ComposerButtons_NeighboursAreSpaceActionApart() =>
            RunSta(() =>
            {
                var gap = ButtonToken("Size.Space.Action");
                var bounds = new Dictionary<string, Rect>();
                ForEachComposerButton(b =>
                {
                    var root = TopVisual(b.Button);
                    bounds[b.Name] = BoundsIn(b.Button, root);
                });
                Assert.Equal(gap, bounds["ClearAllButton"].Left - bounds["DiscardButton"].Right, 0.5);
                Assert.Equal(gap, bounds["MoveButton"].Left - bounds["ReplaceButton"].Right, 0.5);
            });

        [Fact]
        public void ComposerButtons_LabelsAreBodySemiBold() =>
            RunSta(() =>
            {
                var body = ButtonToken("Font.Size.Body");
                ForEachComposerButton(b =>
                {
                    var label = LabelOfButton(b.Button);
                    Assert.Equal(body, label.FontSize);
                    Assert.Equal(FontWeights.SemiBold, label.FontWeight);
                    Assert.Equal(body, b.Button.FontSize);
                    Assert.Equal(FontWeights.SemiBold, b.Button.FontWeight);
                });
            });

        [Theory]
        [InlineData(ThemeMode.Dark)]
        [InlineData(ThemeMode.Light)]
        public void ComposerButtons_BothThemes_HaveAVisibleOutline(ThemeMode mode) =>
            RunSta(() =>
            {
                new ThemeService().SetTheme(mode);
                var visited = 0;
                ForEachComposerButton(b =>
                {
                    visited++;
                    var border = Assert.IsType<SolidColorBrush>(b.Button.BorderBrush).Color;
                    var fill = Assert.IsType<SolidColorBrush>(b.Button.Background).Color;
                    Assert.NotEqual(fill, border);
                    Assert.True(b.Button.BorderThickness.Left >= 2d, $"{b.Name} outline is thinner than 2 px");
                    var drawn = FindAll<Border>(b.Button).First(x => x.Name == "bd");
                    Assert.Equal(border, Assert.IsType<SolidColorBrush>(drawn.BorderBrush).Color);
                    Assert.True(drawn.BorderThickness.Left >= 2d);
                });
                Assert.True(visited >= 9);
            });
    }
}
