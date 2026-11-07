using System;
using System.Globalization;
using AvasRoutingTool.Converters;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>The window-relative cap of the Sources strip (A9 follow-up): a length times a fraction, never a surprise value.</summary>
    public class UiV2FractionOfConverterTests
    {
        [Theory]
        [InlineData(615.0, 0.37, 227.55)]
        [InlineData(952.0, 0.37, 352.24)]
        [InlineData(1000.0, 0.5, 500.0)]
        [InlineData(0.0, 0.37, 0.0)]
        public void FractionOf_ScalesTheLengthByTheParameter(double length, double fraction, double expected) =>
            Assert.Equal(expected, (double)new FractionOfConverter().Convert(length, typeof(double), fraction, CultureInfo.InvariantCulture), 6);

        [Theory]
        [InlineData(500.0, 287.0)]
        [InlineData(1000.0, 370.0)]
        public void FractionOf_NeverGoesBelowItsFloor(double length, double expected) =>
            Assert.Equal(expected, (double)new FractionOfConverter { Floor = 287 }.Convert(length, typeof(double), 0.37, CultureInfo.InvariantCulture), 6);

        [Fact]
        public void FractionOf_AcceptsAnIntOrATextParameter()
        {
            var c = new FractionOfConverter();
            Assert.Equal(500.0, (double)c.Convert(1000.0, typeof(double), 1, CultureInfo.InvariantCulture) / 2, 6);
            Assert.Equal(500.0, (double)c.Convert(1000.0, typeof(double), "0.5", CultureInfo.InvariantCulture), 6);
        }

        [Theory]
        [InlineData("wide", 0.37)]
        [InlineData(null, 0.37)]
        [InlineData(615.0, null)]
        [InlineData(615.0, "not a number")]
        [InlineData(615.0, "")]
        public void FractionOf_GivesNoCapForAnythingItCannotUse(object? value, object? parameter) =>
            Assert.Equal(double.PositiveInfinity, new FractionOfConverter().Convert(value!, typeof(double), parameter!, CultureInfo.InvariantCulture));

        [Fact]
        public void FractionOf_ConvertBack_IsNotSupported() =>
            Assert.Throws<NotSupportedException>(() => new FractionOfConverter().ConvertBack(100.0, typeof(double), 0.37, CultureInfo.InvariantCulture));
    }
}
