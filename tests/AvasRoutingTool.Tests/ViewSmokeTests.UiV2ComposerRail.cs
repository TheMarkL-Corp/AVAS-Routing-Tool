using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Controls;
using AvasRoutingTool.Services;
using AvasRoutingTool.ViewModels;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>Composer right rail: Save &amp; apply follows the action bar standard, the banner shares its radius, and nothing jumps when the banner appears.</summary>
    public partial class ViewSmokeTests
    {
        [Theory]
        [InlineData(1024, 720)]
        [InlineData(1920, 1080)]
        public void ComposerSaveAndApply_FollowsTheActionBarStandard(double w, double h) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, ThemeMode.Dark);
                ShowingAt(view, w, h, () =>
                {
                    var take = (Button)VisibleNamed(view, "TakeButton");
                    var bd = (Border)take.Template.FindName("bd", take);
                    Assert.Equal(6, bd.CornerRadius.TopLeft);
                    Assert.Equal(FontWeights.SemiBold, take.FontWeight);
                    Assert.Equal(Token("Font.Size.Body"), take.FontSize);
                    Assert.InRange(take.ActualHeight, Token("Size.Touch.Min"), 56);
                    Assert.Single(FindAll<MaterialIcon>(take), i => i.IsVisible);
                    var label = FindAll<TextBlock>(take).Single(t => t.IsVisible);
                    Assert.NotEqual(label.Text.ToUpperInvariant(), label.Text);
                });
            });

        [Theory]
        [InlineData(1024, 720)]
        [InlineData(1920, 1080)]
        public void ComposerBanner_SharesTheActionBarRadius_AndALightBorder(double w, double h) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, ThemeMode.Dark);
                ShowingAt(view, w, h, () =>
                {
                    var banner = (Border)VisibleNamed(view, "BannerHost");
                    Assert.Equal(6, banner.CornerRadius.TopLeft);
                    Assert.Equal(1, banner.BorderThickness.Left);
                });
            });

        [Theory]
        [InlineData(1024, 720)]
        [InlineData(1920, 1080)]
        public void ComposerRail_SaveAndApply_StaysPutWhenTheBannerAppears(double w, double h) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                new ThemeService().SetTheme(ThemeMode.Dark);
                var c = rig.Composer("quad_1_3");
                c.Arm(rig.Tx1); c.TapBlock(0); c.Disarm();
                var view = rig.View(c);
                ShowingAt(view, w, h, () =>
                {
                    Assert.Equal(BannerKind.None, c.Banner.Kind);
                    double TakeTop() => BoundsIn(VisibleNamed(view, "TakeButton"), view).Top;
                    double HeaderTop() => BoundsIn(VisibleNamed(view, "StepTakeHeading"), view).Top;
                    var take0 = TakeTop();
                    var header0 = HeaderTop();
                    c.Arm(rig.Vega); // the placing banner
                    Relayout(view);
                    Assert.True(VisibleNamed(view, "BannerHost").IsVisible);
                    Assert.Equal(take0, TakeTop(), 0.5);
                    Assert.Equal(header0, HeaderTop(), 0.5);
                });
            });

        [Theory]
        [InlineData(1024, 720)]
        [InlineData(1920, 1080)]
        public void ComposerRail_ClusterGapsAreAllOneSpaceToken(double w, double h) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, ThemeMode.Dark);
                ShowingAt(view, w, h, () =>
                {
                    var gap = Token("Size.Space.Action");
                    var header = BoundsIn(VisibleNamed(view, "StepTakeHeading"), view);
                    var take = BoundsIn(VisibleNamed(view, "TakeButton"), view);
                    var undo = BoundsIn(VisibleNamed(view, "UndoButton"), view);
                    var discard = BoundsIn(VisibleNamed(view, "DiscardButton"), view);
                    var clear = BoundsIn(VisibleNamed(view, "ClearAllButton"), view);
                    Assert.True(Math.Abs(take.Top - header.Bottom - gap) <= 0.5, $"header to Save: {take.Top - header.Bottom:F1}");
                    var reason = Named(view, "TakeBlockedReasonText").Single();
                    if (reason.IsVisible)
                    {
                        var r = BoundsIn(reason, view);
                        Assert.True(Math.Abs(r.Top - take.Bottom - gap) <= 0.5, $"Save to its reason: {r.Top - take.Bottom:F1}");
                        Assert.True(Math.Abs(discard.Top - r.Bottom - gap) <= 0.5, $"reason to Discard: {discard.Top - r.Bottom:F1}");
                    }
                    else
                        Assert.True(Math.Abs(discard.Top - take.Bottom - gap) <= 0.5, $"Save to Discard: {discard.Top - take.Bottom:F1}");
                    Assert.True(Math.Abs(undo.Top - discard.Bottom - gap) <= 0.5, $"Discard to Undo: {undo.Top - discard.Bottom:F1}");
                    Assert.Equal(gap, clear.Left - discard.Right, 0.5); // the pair is two 4 px margins
                });
            });

        // Bug: Undo is Hidden (not Collapsed) so Save & apply never moves, but it sat between Save and Discard, so with no Undo offered
        // the rail showed a blank 56 px hole. Undo now goes last: the hole, when there is one, is empty space at the foot of the rail.
        [Theory]
        [InlineData(1024, 720)]
        [InlineData(1920, 1080)]
        public void ComposerRail_NoBlankSlotBetweenSaveAndDiscard_WhenUndoIsNotOffered(double w, double h) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                new ThemeService().SetTheme(ThemeMode.Dark);
                var c = rig.Composer("quad_1_3");
                c.Arm(rig.Tx1); c.TapBlock(0); c.Disarm();
                var view = rig.View(c);
                ShowingAt(view, w, h, () =>
                {
                    Assert.False(c.CanUndo);
                    var gap = Token("Size.Space.Action");
                    var above = Named(view, "TakeBlockedReasonText").Single() is { IsVisible: true } reason
                        ? BoundsIn(reason, view) : BoundsIn(VisibleNamed(view, "TakeButton"), view);
                    var discard = BoundsIn(VisibleNamed(view, "DiscardButton"), view);
                    Assert.True(Math.Abs(discard.Top - above.Bottom - gap) <= 0.5, $"{discard.Top - above.Bottom:F1} px between Save & apply and Discard");
                });
            });
    }
}
