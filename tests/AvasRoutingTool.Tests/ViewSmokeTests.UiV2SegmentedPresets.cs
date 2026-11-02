using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Services;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// UI v2 segmented layout control on the display card's action bar (plan task A10): the four one-tap presets and the Layout
    /// dropdown are one joined segmented control; each preset draws its layout as a <see cref="LayoutGlyph"/> (outline cells in the
    /// button's text colour); Copy layout stands apart at the right of the group.
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

        private static List<Button> VisiblePresets(DependencyObject view) =>
            Named(view, "InlinePreset").Cast<Button>().Where(b => b.IsVisible).ToList();

        /// <summary>The outline cells of a preset's glyph: the bordered blocks (the glyph has no other borders).</summary>
        private static List<System.Windows.Controls.Border> GlyphCells(DependencyObject preset) =>
            FindAll<System.Windows.Controls.Border>(FindAll<LayoutGlyph>(preset).Single()).Where(b => b.BorderThickness.Left > 0).ToList();

        [Fact]
        public void LayoutSegments_JoinsThePresetsAndLayout_IntoOneControl_EachSegmentAtLeast48() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    var group = (Grid)Named(view, "LayoutSegments").Single();
                    Assert.True(Within(Named(view, "InlinePresets").Single(), group), "the presets are not inside LayoutSegments");
                    var layout = (Button)Named(view, "LayoutButton").Single();
                    Assert.True(Within(layout, group), "Layout is not inside LayoutSegments");

                    var presets = VisiblePresets(view);
                    Assert.Equal(4, presets.Count);
                    var segments = presets.Cast<FrameworkElement>().Append(layout).ToList();
                    foreach (var segment in segments)
                    {
                        Assert.True(segment.ActualWidth >= Token("Size.Touch.Min") && segment.ActualHeight >= Token("Size.Touch.Min"),
                            $"{segment.Name} is {segment.ActualWidth}x{segment.ActualHeight}");
                    }
                    for (int i = 1; i < segments.Count; i++)
                    {
                        var before = BoundsIn(segments[i - 1], group);
                        var after = BoundsIn(segments[i], group);
                        Assert.True(Math.Abs(after.Left - before.Right) <= 0.5, $"segment {i} starts at {after.Left:F1}; the one before ends at {before.Right:F1}");
                    }
                });
            });

        [Fact]
        public void LayoutSegments_HaveOneOutline_AndNoSegmentBorder() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    var group = (Grid)Named(view, "LayoutSegments").Single();
                    var outline = (System.Windows.Controls.Border)Named(view, "LayoutSegmentsOutline").Single();
                    Assert.Equal(1.0, outline.BorderThickness.Left);
                    Assert.Null(outline.Background);
                    Assert.False(outline.IsHitTestVisible);
                    Assert.True(Within(outline, group), "the outline does not overlay the segments");

                    // The outline is the only border with a width in the group: the glyph cells are the presets' drawing, and every
                    // segment's own border part is 0 (its focus ring is collapsed).
                    var glyphs = FindAll<LayoutGlyph>(group);
                    var drawn = FindAll<System.Windows.Controls.Border>(group)
                        .Where(b => b.IsVisible && b.BorderThickness.Left > 0 && !glyphs.Any(g => Within(b, g))).ToList();
                    Assert.Same(outline, Assert.Single(drawn));
                    foreach (var button in Named(view, "InlinePreset").Concat(Named(view, "LayoutButton")).Cast<Button>())
                    {
                        var bd = (System.Windows.Controls.Border)button.Template.FindName("bd", button);
                        Assert.Equal(0.0, bd.BorderThickness.Left);
                    }
                });
            });

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
        public void LayoutSegments_EveryPreset_AndLayout_TakeAPressOnTheirWholeArea_IncludingTheOuterRing() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    // Four presets and Layout. A press anywhere in the 48x48 segment must land on the segment, not only in its inset block.
                    var segments = VisiblePresets(view).Cast<Button>().Append((Button)Named(view, "LayoutButton").Single()).ToList();
                    Assert.Equal(5, segments.Count);
                    foreach (var segment in segments)
                    {
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
                                $"{segment.Name} {w:F0}x{h:F0}: a press at ({spot.X:F1},{spot.Y:F1}) lands on {landsOn}, not on the segment");
                        }
                    }
                });
            });

        [Fact]
        public void InlinePresets_EachDrawsItsLayoutAsALayoutGlyph_At32x20_WithOutlineCellsAndNoText() =>
            RunSta(() =>
            {
                Assert.Equal(32.0, Token("Size.Schematic.Inline.Width"));
                Assert.Equal(20.0, Token("Size.Schematic.Inline.Height"));

                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    var presets = VisiblePresets(view);
                    Assert.Equal(new[] { 1, 2, 2, 4 }, presets.Select(p => FindAll<LayoutGlyph>(p).Single().CellCount));
                    foreach (var preset in presets)
                    {
                        var glyph = FindAll<LayoutGlyph>(preset).Single();
                        Assert.Equal(32.0, glyph.ActualWidth);
                        Assert.Equal(20.0, glyph.ActualHeight);
                        Assert.Empty(FindAll<TextBlock>(glyph));
                        Assert.All(FindAll<System.Windows.Controls.Border>(glyph), b => Assert.True(b.Background == null || ColorOf(b.Background).A == 0, "a glyph cell is filled"));
                    }
                });
            });

        [Fact]
        public void InlinePreset_ShowsTheLayoutOnScreen_InBothThemes() =>
            RunSta(() =>
            {
                var theme = new ThemeService();
                try
                {
                    foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light })
                    {
                        theme.SetTheme(mode);
                        var rig = new CopyRig();
                        var view = rig.View(rig.Rx1, out var host, 613);
                        Showing(host, () =>
                        {
                            // Rx1 shows its Matrix 2x2: the selected fill, the accent glyph and the check mark; the other three are plain.
                            var presets = VisiblePresets(view);
                            Assert.Equal(4, presets.Count);
                            foreach (var preset in presets)
                            {
                                string id = ((LayoutItemViewModel)preset.DataContext).Id;
                                var cells = GlyphCells(preset);
                                Assert.NotEmpty(cells);
                                if (id == "matrix2x2")
                                {
                                    Assert.Equal(PaletteColor("Brush.Surface.Selected"), ColorOf(preset.Background));
                                    Assert.True(CheckIsShown(preset), $"{mode}: the on-screen preset has no visible checkmark");
                                    Assert.All(cells, c => Assert.Equal(PaletteColor("Brush.Accent.Primary"), ColorOf(c.BorderBrush)));
                                }
                                else
                                {
                                    Assert.Equal(0, ColorOf(preset.Background).A);
                                    Assert.False(CheckIsShown(preset), $"{mode}: {id} shows a checkmark but is not on screen");
                                    Assert.All(cells, c => Assert.Equal(PaletteColor("Brush.Text.Secondary"), ColorOf(c.BorderBrush)));
                                }
                            }
                        });
                    }
                }
                finally { theme.SetTheme(ThemeMode.Dark); }
            });

        [Fact]
        public void LayoutButton_IsAnIconAndAChevron_NoText_WithTheMoreLayoutsTooltip() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    var layout = (Button)Named(view, "LayoutButton").Single();
                    // Icon and chevron only: the SymbolIcon's own glyph text is the chevron, no words.
                    Assert.Empty(FindAll<TextBlock>(layout).Where(t => !string.IsNullOrEmpty(t.Text) && !FindAll<SymbolIcon>(layout).Any(i => i.IsAncestorOf(t))));
                    var icon = (System.Windows.Shapes.Path)Named(view, "LayoutIcon").Single();
                    Assert.Same(Application.Current.FindResource("Icon.Grid"), icon.Data);
                    Assert.Contains(FindAll<SymbolIcon>(layout), i => i.Symbol == SymbolRegular.ChevronDown24);
                    Assert.Equal("More layouts", layout.ToolTip as string);
                    Assert.Equal("Layout for RX1_c059e_SFPA", System.Windows.Automation.AutomationProperties.GetName(layout));
                });

                // At the minimum card width the presets are gone and Layout is the only segment left, still a touch target.
                var narrow = new CopyRig();
                var narrowView = narrow.View(narrow.Rx1, out var narrowHost, Token("Size.Card.Min"));
                Showing(narrowHost, () =>
                {
                    Assert.Empty(VisiblePresets(narrowView));
                    var layout = (Button)Named(narrowView, "LayoutButton").Single();
                    Assert.True(layout.IsVisible && layout.ActualWidth >= Token("Size.Touch.Min") && layout.ActualHeight >= Token("Size.Touch.Min"),
                        $"Layout is {layout.ActualWidth}x{layout.ActualHeight} at the minimum width");
                });
            });

        [Fact]
        public void InlinePresets_HaveTheirShortNames_TooltipsAndHelpText() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    var presets = Named(view, "InlinePreset").Cast<Button>().ToList();
                    Assert.Equal(new[] { "single", "pip", "side_by_side", "matrix2x2" }, presets.Select(p => ((LayoutItemViewModel)p.DataContext).Id));
                    Assert.Equal(new[] { "Single view", "Picture-in-picture", "Side by side", "Quad" },
                        presets.Select(p => System.Windows.Automation.AutomationProperties.GetName(p)));
                    Assert.Equal(new[] { "Single view", "Picture-in-picture", "Side by side", "Quad" }, presets.Select(p => p.ToolTip as string));
                    string composer = "Opens the Composer. Nothing changes on screen until Take.";
                    Assert.Equal(new[] { string.Empty, composer, composer, composer },
                        presets.Select(p => System.Windows.Automation.AutomationProperties.GetHelpText(p)));
                });
            });

        [Fact]
        public void CopyLayout_IsAGhostButtonOutsideTheSegments_WithIconAndLabelSideBySide() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx1 = rig.View(rig.Rx1, out var h1);
                var rx3 = rig.View(rig.Rx3, out var h3);
                Showing(new StackPanel { Children = { h1, h3 } }, () =>
                {
                    var handle = (Button)Named(rx1, "CopyLayoutHandle").Single();
                    Assert.Same(Application.Current.FindResource("Style.Button.CopyLayout"), handle.Style);
                    Assert.Equal(Token("Size.Touch.Min"), handle.MinHeight);
                    Assert.True(handle.ActualHeight >= Token("Size.Touch.Min"), $"Copy layout is {handle.ActualHeight} high");
                    Assert.False(Within(handle, Named(rx1, "LayoutSegments").Single()), "Copy layout is inside the segmented control");

                    var label = FindAll<TextBlock>(handle).Single(t => t.Text == "Copy layout");
                    var icon = FindAll<System.Windows.Shapes.Path>(handle).Single(p => p.Name == "CopyIcon");
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
