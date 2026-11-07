using System;
using System.Linq;
using System.Windows;
using AvasRoutingTool.Controls;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>Round 3, display card header: the pencil and info glyphs are the 20 px icon size of the action bar, and the mode badge sits one 8 px gap clear of the info glyph.</summary>
    public partial class ViewSmokeTests
    {
        [Theory]
        [InlineData(1024, 720)]
        [InlineData(1920, 952)]
        public void DisplayCardHeader_GlyphsAre20px_AndTheModeBadgeIs8pxClearOfTheInfoGlyph(double width, double height) =>
            RunSta(() =>
            {
                var (view, _) = FullHdOverview(sources: 3, displays: 5);
                ShowingAt(view, width, height, () =>
                {
                    Idle();
                    Relayout(view);
                    var card = FindAll<DisplayCardView>(view).First(c => c.IsVisible);
                    var size = (double)Application.Current.FindResource("Size.Icon.Layout");
                    var gap = (double)Application.Current.FindResource("Size.Space.Action");
                    var pencilIcon = FindAll<MaterialIcon>((FrameworkElement)Named(card, "RenamePencil").Single()).Single();
                    var infoIcon = FindAll<MaterialIcon>((FrameworkElement)Named(card, "DeviceInfoBadge").Single()).Single();
                    Assert.Equal(size, pencilIcon.ActualWidth, 0.5);
                    Assert.Equal(size, infoIcon.ActualWidth, 0.5);
                    var badge = BoundsIn((FrameworkElement)Named(card, "ModeBadge").Single(), view);
                    var info = BoundsIn(infoIcon, view);
                    Assert.True(badge.Left - info.Right >= gap - 0.5, $"the mode badge is {badge.Left - info.Right:F1} px from the info glyph (needs >= {gap})");
                });
            });
    }
}
