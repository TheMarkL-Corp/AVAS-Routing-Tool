using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>UI v2 say-it-once (critique M4): each fact on a card is said in one place.</summary>
    public partial class ViewSmokeTests
    {
        [Fact]
        public void DisplayCard_UnroutedFooter_IsEmpty_TheTileSaysReadyToRoute() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx3 = rig.View(rig.Rx3, out var h3);
                var rx1 = rig.View(rig.Rx1, out var h1);
                var host = new StackPanel { Children = { h1, h3 } };
                Showing(host, () =>
                {
                    // Unrouted: the dock says nothing (the tile says it), and no SOURCE line.
                    Assert.Equal(string.Empty, Named(rx3, "SourceFooter").Cast<TextBlock>().Single().Text);
                    Assert.Empty(VisibleTexts(rx3, "SOURCE:"));

                    // Exactly one visible "Ready to route a source" on the card: it is the tile's, inside PreviewSurface.
                    var ready = Assert.Single(VisibleTexts(rx3, "Ready to route a source"));
                    Assert.Contains(FindAll<TextBlock>(EmptyTile(rx3)), t => ReferenceEquals(t, ready));
                    AssertInside(ready, (FrameworkElement)Named(rx3, "PreviewSurface").Single());

                    // Routed (RX1 runs a Matrix 2x2): the source line.
                    AssertVisibleText(rx1, "SOURCE: ");
                });
            });

        [Fact]
        public void DisplayCard_CopyButton_ExplainsWhyDisabled_ByTooltipAndHelpText() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx3 = rig.View(rig.Rx3, out var host);
                Showing(host, () =>
                {
                    var handle = (Button)Named(rx3, "CopyLayoutHandle").Single();
                    Assert.False(handle.IsEnabled);
                    Assert.False(string.IsNullOrEmpty(rig.Rx3.CopyFromBlockedText));
                    Assert.Equal(rig.Rx3.CopyFromBlockedText, (string?)handle.ToolTip);
                    Assert.Equal(rig.Rx3.CopyFromBlockedText, AutomationProperties.GetHelpText(handle));

                    // The card no longer repeats the reason in its dock.
                    Assert.Empty(Named(rx3, "CopyFromBlockedLabel"));
                });
            });
    }
}
