using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AvasRoutingTool.Controls;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>UI v2 display card (plan tasks A9-A11): row tokens, ui:Card frame, action bar, overlays, Undo toast and Layout menu.</summary>
    public partial class ViewSmokeTests
    {
        private static double Token(string key) => (double)Application.Current.FindResource(key);

        [Fact]
        public void DisplayCard_ChromeHeight_EqualsTheSumOfTheRowTokens() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host);
                Showing(host, () =>
                {
                    var rows = ((Grid)Named(view, "CardGrid").Single()).RowDefinitions;
                    Assert.Equal(Token("Size.Card.Row.Header"), rows[0].Height.Value);
                    Assert.Equal(Token("Size.Card.Row.ActionBar"), rows[1].Height.Value);
                    Assert.True(rows[2].Height.IsStar);
                    Assert.Equal(Token("Size.Card.Row.Dock"), rows[3].Height.Value);
                    // Frame border 2 + padding 24 = 26 around the rows (AdaptiveCardPanel sizes cards from this sum before measuring).
                    Assert.Equal(GridSpec.DisplayCard.ChromeHeight, rows[0].Height.Value + rows[1].Height.Value + rows[3].Height.Value + 26);
                    Assert.Equal(Token("Size.Card.Min"), view.MinWidth);
                });
            });

        [Fact]
        public void DisplayCard_Frame_IsAnUiCard_AndTheHeaderIsMadeOfBadgesAndIcons() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host);
                Showing(host, () =>
                {
                    var frame = Named(view, "CardFrame").Single();
                    Assert.IsType<Card>(frame);
                    Assert.Same(Application.Current.FindResource("Style.Card.Display"), frame.Style);
                    Assert.IsType<Badge>(Named(view, "ModeBadge").Single());
                    Assert.Equal(Token("Size.Dot.S"), FindAll<System.Windows.Shapes.Ellipse>(view).First().ActualWidth);

                    var info = Named(view, "DeviceInfoBadge").Single();
                    Assert.True(info.Focusable && System.Windows.Input.KeyboardNavigation.GetIsTabStop(info));
                    Assert.Equal(SymbolRegular.Info24, FindAll<SymbolIcon>(info).Single().Symbol);
                    Assert.Equal("Device network telemetry: IP, MAC", System.Windows.Automation.AutomationProperties.GetName(info));
                    Assert.NotNull(info.ToolTip);
                });
            });

        [Fact]
        public void DisplayCard_ActionBar_HasTouchSizedButtons_WithIcons_AndNoGlyphCharacters() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                rig.Rx1.HasDraft = true;
                var view = rig.View(rig.Rx1, out var host);
                Showing(host, () =>
                {
                    foreach (var name in new[] { "LayoutButton", "CopyLayoutHandle", "DraftChip", "TurnOffButton" })
                    {
                        var b = (Button)Named(view, name).Single();
                        Assert.True(b.IsVisible && b.ActualHeight >= 48 && b.ActualWidth >= 48, $"{name} {b.ActualWidth}x{b.ActualHeight}");
                    }
                    var turnOff = (Button)Named(view, "TurnOffButton").Single();
                    Assert.Equal(Token("Size.Touch.Min"), turnOff.MinWidth);
                    Assert.Equal("power_settings_new", FindAll<AvasRoutingTool.Controls.MaterialIcon>(turnOff).Single().Kind);
                    Assert.Equal(Token("Size.Icon.Layout"), FindAll<AvasRoutingTool.Controls.MaterialIcon>(turnOff).Single().Width);

                    // The chevron after "More layouts" is a vector icon (Material expand_more), not a character; the grid icon is another.
                    var layout = Named(view, "LayoutButton").Single();
                    Assert.Contains(FindAll<AvasRoutingTool.Controls.MaterialIcon>(layout), i => i.Kind == "expand_more");
                    Assert.Contains(FindAll<AvasRoutingTool.Controls.MaterialIcon>(layout), i => i.Kind == "grid_view");

                    // The draft chip keeps the shared staged-chip template (part DraftFrame).
                    var chip = (Button)Named(view, "DraftChip").Single();
                    Assert.NotNull(chip.Template.FindName("DraftFrame", chip));

                    foreach (var tb in FindAll<TextBlock>(view).Where(t => t.IsVisible && !string.IsNullOrEmpty(t.Text) && FindAll<SymbolIcon>(view).All(i => i.Glyph != t.Text)))
                    {
                        Assert.DoesNotContain(tb.Text, ch => ch >= '←' && ch <= '➿' && ch != '✓');
                        Assert.True(tb.FontSize >= 12, $"'{tb.Text}' is {tb.FontSize}px");
                    }
                });
            });

        [Fact]
        public void DisplayCard_BodyOverlays_ShareTheOverlayStyle_AndTheLockIsASymbolIcon() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
                var view = rig.View(rig.Rx3, out var host);
                Showing(host, () =>
                {
                    var overlay = Application.Current.FindResource("Style.Overlay.Body");
                    foreach (var name in new[] { "DropChoicePanel", "CopyTargetAllowedPanel", "CopyTargetBlockedPanel" })
                        Assert.Same(overlay, Named(view, name).Single().Style);

                    var lock1 = Assert.IsType<SymbolIcon>(Named(view, "CopyTargetLock").Single());
                    Assert.Equal(SymbolRegular.LockClosed24, lock1.Symbol);
                    Assert.Equal(Token("Size.Icon.L"), lock1.FontSize);
                    Assert.True(lock1.IsVisible);
                    Assert.False(string.IsNullOrEmpty(lock1.Glyph));
                    Assert.DoesNotContain("Icon.Lock", File.ReadAllText(Path.Combine(UiV2Allowlist.SourceRoot(), "Views", "DisplayCardView.xaml")));
                });
            });

        [Fact]
        public void DisplayCard_PictureStates_FollowTheViewState_AndNoticeIconsUseTheStateToken() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx3, out var host); // unrouted
                Showing(host, () =>
                {
                    AssertVisibleText(view, "Ready to route a source");
                    Assert.Empty(VisibleTexts(view, "Routed to Source"));
                    // The empty tile shows the brand mark, not the monitor state icon; the state icon (routed non-AVAS / external notices) keeps the token size.
                    var stateStyle = (Style)Application.Current.FindResource("Style.Icon.State");
                    Assert.DoesNotContain(FindAll<System.Windows.Shapes.Path>(view), p => p.IsVisible && p.Style == stateStyle);
                    Assert.Contains(FindAll<BrandMark>(view), b => b.IsVisible);
                    Assert.Equal(Token("Size.Icon.L"), (double)stateStyle.Setters.OfType<Setter>().Single(s => s.Property == FrameworkElement.WidthProperty).Value);
                    foreach (var tb in FindAll<TextBlock>(view).Where(t => t.IsVisible && !string.IsNullOrWhiteSpace(t.Text)))
                        Assert.True(tb.FontSize >= 12, $"'{tb.Text}' is {tb.FontSize}px");
                });
            });

        [Fact]
        public void DisplayCard_UndoToast_UsesTheTokens_AndLayoutMenuMarksTheTileInWordsWithAnIcon() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
                Pump(() => rig.Vm.CopyDisplayAsync(rig.Rx1, rig.Pax));
                rig.Vm.DisarmDisplay();
                var paxView = rig.View(rig.Pax, out var paxHost);
                Showing(paxHost, () =>
                {
                    var toast = (FrameworkElement)Named(paxView, "UndoToast").Single();
                    Assert.True(toast.IsVisible);
                    Assert.True(toast.ActualHeight >= Token("Size.Toast.Min"), $"toast {toast.ActualHeight}");
                    Assert.True(((Button)Named(paxView, "UndoScreenButton").Single()).ActualHeight >= 48);
                    var picture = Named(paxView, "PreviewSurface").Single();
                    Assert.True(BoundsIn(toast, paxView).Top >= BoundsIn(picture, paxView).Bottom - 0.5, "the toast lies on the picture");
                });

                var view = rig.View(rig.Rx1, out var host);
                Showing(host, () =>
                {
                    Click(Named(view, "LayoutButton").Single());
                    var popup = (Popup)Named(view, "LayoutMenuPopup").Single();
                    var menu = (FrameworkElement)popup.Child;
                    Relayout(menu);
                    Assert.Equal(Token("Size.Popup.LayoutMenu"), Named(menu, "LayoutMenuFrame").Concat(new[] { menu }).First().Width);
                    var tiles = Named(menu, "LayoutMenuTile").Cast<Button>().ToList();
                    Assert.All(tiles, t => Assert.True(t.ActualHeight >= Token("Size.LayoutTile.Min"), $"tile {t.ActualHeight}"));
                    Assert.All(tiles, t => Assert.Same(Application.Current.FindResource("Style.Button.LayoutMenuTile"), t.Style));
                    Assert.All(tiles, t => Assert.NotNull(t.Template.FindName("bd", t)));

                    var marked = tiles.Where(t => VisibleTexts(t, "On screen").Count > 0).ToList();
                    var tile = Assert.Single(marked);
                    Assert.Equal("matrix2x2", ((LayoutItemViewModel)tile.DataContext).Id);
                    Assert.Contains(FindAll<SymbolIcon>(tile), i => i.Symbol == SymbolRegular.Checkmark24 && i.IsVisible);
                    var accent = ((System.Windows.Media.SolidColorBrush)Application.Current.FindResource("Brush.Accent.Primary")).Color;
                    Assert.Equal(accent, ((System.Windows.Media.SolidColorBrush)((Border)tile.Template.FindName("bd", tile)).BorderBrush).Color);

                    var schematic = FindAll<LayoutSchematic>(tiles[0]).Single();
                    Assert.Equal(Token("Size.Schematic.Menu.Width"), schematic.Width);
                    Assert.Equal(Token("Size.Schematic.Menu.Height"), schematic.Height);
                });
            });

        [Fact]
        public void DisplayCard_AnUnroutedDisplay_MarksNoLayoutTile() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx3, out var host);
                Showing(host, () =>
                {
                    Click(Named(view, "LayoutButton").Single());
                    var menu = (FrameworkElement)((Popup)Named(view, "LayoutMenuPopup").Single()).Child;
                    Relayout(menu);
                    Assert.Empty(Named(menu, "LayoutMenuTile").Where(t => VisibleTexts(t, "On screen").Count > 0));
                });
            });
    }
}
