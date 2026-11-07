using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Controls;
using AvasRoutingTool.Services;
using AvasRoutingTool.Views;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Round 3 follow-up: the draft chip ("DRAFT · Resume") and the two Stop buttons (the banner's Stop while a TX source is armed, and the
    /// source card's Stop preview) follow the same button standard as the action bar: Size.Action.PadX each side, Size.Space.Action between
    /// an icon and its label, and the semibold body label.
    /// </summary>
    public partial class ViewSmokeTests
    {
        [Fact]
        public void DraftChip_UsesTheActionBarPadding_AndTouchHeight() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                rig.Rx1.HasDraft = true;
                var view = rig.View(rig.Rx1, out var host, 480);
                Showing(host, () =>
                {
                    var chip = (Button)Named(view, "DraftChip").Single();
                    Assert.True(chip.IsVisible);
                    Assert.Equal(Token("Size.Action.PadX"), chip.Padding.Left);
                    Assert.Equal(Token("Size.Action.PadX"), chip.Padding.Right);
                    Assert.True(chip.ActualHeight >= Token("Size.Touch.Min"), $"chip is {chip.ActualHeight} high");

                    // The padding is drawn: the template puts the label PadX in from the frame, not a fixed 4 px.
                    var label = FindAll<TextBlock>(chip).Single(t => t.Text.StartsWith("DRAFT"));
                    double left = BoundsIn(label, chip).Left;
                    Assert.True(left >= Token("Size.Action.PadX") - 0.5, $"the label starts {left:F1} px in; the standard is {Token("Size.Action.PadX")}");
                    Assert.Equal(System.Windows.FontWeights.SemiBold, label.FontWeight);
                });
            });

        [Fact]
        public void DraftChip_IsAnOutlinedActionBarButton_WithIcon_OwnFocusRing_AndOneLineLabel() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                rig.Rx1.HasDraft = true;
                var view = rig.View(rig.Rx1, out var host, 480);
                Showing(host, () =>
                {
                    var chip = (Button)Named(view, "DraftChip").Single();
                    var outlined = Application.Current.FindResource("Style.Button.ActionBar.Outlined");
                    var style = (Style)chip.Style;
                    Assert.True(ReferenceEquals(style.BasedOn, outlined), "the draft chip is not based on Style.Button.ActionBar.Outlined");

                    var bd = (Border)chip.Template.FindName("bd", chip);
                    Assert.Equal(6.0, bd.CornerRadius.TopLeft);
                    Assert.Equal(2.0, bd.BorderThickness.Left);
                    var ring = (Border)chip.Template.FindName("FocusRing", chip);
                    Assert.Equal(6.0, ring.CornerRadius.TopLeft);
                    Assert.Equal(System.Windows.Visibility.Collapsed, ring.Visibility);
                    chip.SetValue(InputModality.ShowsFocusProperty, true);
                    Assert.Equal(System.Windows.Visibility.Visible, ring.Visibility);
                    Assert.Same(Application.Current.FindResource("Brush.Status.Staged"), bd.BorderBrush);

                    var icon = FindAll<MaterialIcon>(chip).Single();
                    var label = FindAll<TextBlock>(chip).Single(t => t.Text.StartsWith("DRAFT"));
                    double gap = BoundsIn(label, chip).Left - BoundsIn(icon, chip).Right;
                    Assert.True(Math.Abs(gap - Token("Size.Space.Action")) <= 0.5, $"icon-to-label gap is {gap:F1}");
                    Assert.Equal(System.Windows.TextWrapping.NoWrap, label.TextWrapping);
                    Assert.Equal(System.Windows.TextTrimming.CharacterEllipsis, label.TextTrimming);
                });
            });

        [Fact]
        public void DraftChip_IconMeansResume_NotTheComposersUndoGlyph() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                rig.Rx1.HasDraft = true;
                var view = rig.View(rig.Rx1, out var host, 480);
                Showing(host, () =>
                {
                    var chip = (Button)Named(view, "DraftChip").Single();
                    var icon = FindAll<MaterialIcon>(chip).Single();
                    Assert.NotEqual("undo", icon.Kind); // undo is the Composer's Undo button
                    Assert.Equal("play_arrow", icon.Kind); // not "edit": that is the rename pencil
                });
            });

        [Fact]
        public void DraftChip_StagedOutline_Reaches3To1_OnItsFill_AndTheCard_InTheLightTheme() =>
            RunSta(() =>
            {
                try
                {
                    new ThemeService().SetTheme(ThemeMode.Light);
                    var outline = BrushColour("Brush.Status.Staged");
                    var ratioFill = RatioOf(outline, BrushColour("Brush.Surface.Staged"));
                    var ratioCard = RatioOf(outline, BrushColour("Brush.Surface.Card"));
                    Assert.True(ratioFill >= 3.0, $"amber outline on the chip fill {ratioFill:F2}:1");
                    Assert.True(ratioCard >= 3.0, $"amber outline on the card {ratioCard:F2}:1");
                }
                finally { new ThemeService().SetTheme(ThemeMode.Dark); }
            });

        [Fact]
        public void ArmBanner_StopButton_IsAnActionBarButton_WithIconAndLabel() =>
            RunSta(() =>
            {
                var (vm, _, sources) = Overview();
                vm.ArmSource(sources[1]);
                var view = new VisualRoutingView { DataContext = vm };
                Showing(view, () =>
                {
                    var stop = (Button)Named(view, "StopPlacingButton").Single();
                    Assert.True(stop.IsVisible);
                    var style = (Style)stop.Style;
                    var bar = Application.Current.FindResource("Style.Button.ActionBar");
                    Assert.True(ReferenceEquals(style, bar) || ReferenceEquals(style.BasedOn, bar), "the banner Stop is not based on Style.Button.ActionBar");

                    Assert.Equal(Token("Size.Action.PadX"), stop.Padding.Left);
                    Assert.Equal(Token("Size.Action.PadX"), stop.Padding.Right);
                    Assert.Equal(Token("Size.Touch.Min"), stop.ActualHeight);

                    var icon = FindAll<MaterialIcon>(stop).Single();
                    var label = FindAll<TextBlock>(stop).Single(t => t.Text == "Stop");
                    Assert.Equal("stop", icon.Kind);
                    double gap = BoundsIn(label, stop).Left - BoundsIn(icon, stop).Right;
                    Assert.True(Math.Abs(gap - Token("Size.Space.Action")) <= 0.5, $"icon-to-label gap is {gap:F1}, not Size.Space.Action");
                    Assert.True(label.FontSize >= 12);
                });
            });

        [Fact]
        public void SourceCard_StopAndStart_UseTheStandardPaddingAndIconGap() =>
            RunSta(() =>
            {
                var vm = UiV2Source();
                vm.IsStreaming = true;
                SourceCardAt(vm, view =>
                {
                    var stop = (Button)Named(view, "StopStreamButton").Single();
                    Assert.True(stop.IsVisible);
                    Assert.Equal(Token("Size.Action.PadX"), stop.Padding.Left);
                    Assert.Equal(Token("Size.Action.PadX"), stop.Padding.Right);
                    var icon = FindAll<MaterialIcon>(stop).Single();
                    var label = FindAll<TextBlock>(stop).Single(t => t.Text == "Stop");
                    double gap = BoundsIn(label, stop).Left - BoundsIn(icon, stop).Right;
                    Assert.True(Math.Abs(gap - Token("Size.Space.Action")) <= 0.5, $"icon-to-label gap is {gap:F1}, not Size.Space.Action");
                    Assert.Equal(System.Windows.FontWeights.SemiBold, label.FontWeight);

                    var refresh = (Button)Named(view, "RefreshStreamButton").Single();
                    double between = BoundsIn(stop, (FrameworkElement)stop.Parent).Left - BoundsIn(refresh, (FrameworkElement)stop.Parent).Right;
                    Assert.True(Math.Abs(between - Token("Size.Space.Action")) <= 0.5, $"gap between Refresh and Stop is {between:F1}");
                });
                vm.IsStreaming = false;
                SourceCardAt(vm, view =>
                {
                    var start = (Button)Named(view, "StartStreamButton").Single();
                    Assert.True(start.IsVisible);
                    Assert.Equal(Token("Size.Action.PadX"), start.Padding.Left);
                    var icon = FindAll<MaterialIcon>(start).Single();
                    var label = FindAll<TextBlock>(start).Single(t => t.Text == "Start");
                    double gap = BoundsIn(label, start).Left - BoundsIn(icon, start).Right;
                    Assert.True(Math.Abs(gap - Token("Size.Space.Action")) <= 0.5, $"icon-to-label gap is {gap:F1}, not Size.Space.Action");
                });
            });
    }
}
