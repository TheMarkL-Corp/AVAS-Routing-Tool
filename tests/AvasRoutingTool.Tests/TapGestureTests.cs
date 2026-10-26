using System.Windows;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>Tap versus drag (spec 6: 8 px movement before a drag starts), as the source cards and blocks use it.</summary>
    public class TapGestureTests
    {
        [Fact]
        public void PressAndRelease_WithoutMoving_IsATap()
        {
            var g = new TapGesture();
            g.Press(new Point(100, 100));
            Assert.False(g.Move(new Point(105, 96))); // within 8 px
            Assert.True(g.Release());
        }

        [Fact]
        public void MovingMoreThan8Px_StartsADragOnce_AndTheReleaseIsNoTap()
        {
            var g = new TapGesture();
            g.Press(new Point(100, 100));
            Assert.True(g.Move(new Point(109, 100)));
            Assert.False(g.Move(new Point(130, 100))); // already dragging
            Assert.False(g.Release());
        }

        [Fact]
        public void ReleaseWithoutPress_IsNoTap_AndMoveWithoutPressStartsNothing()
        {
            var g = new TapGesture();
            Assert.False(g.Move(new Point(500, 500)));
            Assert.False(g.Release());
        }

        [Fact]
        public void Threshold_Is8Px() => Assert.Equal(8, TapGesture.DragThreshold);
    }
}
