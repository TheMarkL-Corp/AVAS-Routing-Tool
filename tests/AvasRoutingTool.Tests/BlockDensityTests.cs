using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    [Collection("WpfSta")]
    public class BlockDensityTests
    {
        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("STA failure: " + failure);
        }

        [Fact]
        public void IsCompact_DefaultsToFalse() => RunSta(() =>
        {
            Assert.False(BlockDensity.GetIsCompact(new StackPanel()));
        });

        [Fact]
        public void IsCompact_InheritsToDescendants() => RunSta(() =>
        {
            var text = new TextBlock();
            var inner = new StackPanel { Children = { text } };
            var outer = new StackPanel { Children = { inner } };
            var window = new Window { Content = outer };
            try
            {
                BlockDensity.SetIsCompact(outer, true);
                Assert.True(BlockDensity.GetIsCompact(text));
                BlockDensity.SetIsCompact(outer, false);
                Assert.False(BlockDensity.GetIsCompact(text));
            }
            finally { window.Close(); }
        });

        [Fact]
        public void CompactBelow_Is240() => Assert.Equal(240d, BlockDensity.CompactBelow);

        [Theory]
        [InlineData(239.9, true)]
        [InlineData(240.0, false)]
        [InlineData(0.0, false)]
        [InlineData(560.0, false)]
        public void LessThanConverter_WithCompactBelow_IsTrueBelow240Only(double width, bool expected)
        {
            var converter = new LessThanConverter();
            Assert.Equal(expected, converter.Convert(width, typeof(bool), BlockDensity.CompactBelow, CultureInfo.InvariantCulture));
        }
    }
}
