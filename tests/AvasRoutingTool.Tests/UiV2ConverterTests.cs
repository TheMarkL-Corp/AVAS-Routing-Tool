using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using AvasRoutingTool.Converters;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>The shared view converters (UI v2): visibility by rule, "wide enough", and a bool that picks a grid length.</summary>
    public class UiV2ConverterTests
    {
        private static object Convert(VisibilityConverter c, object? value) =>
            c.Convert(value!, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        [Theory]
        [InlineData(VisibilityRule.True, true, Visibility.Visible)]
        [InlineData(VisibilityRule.True, false, Visibility.Collapsed)]
        [InlineData(VisibilityRule.True, null, Visibility.Collapsed)]
        [InlineData(VisibilityRule.False, true, Visibility.Collapsed)]
        [InlineData(VisibilityRule.False, false, Visibility.Visible)]
        [InlineData(VisibilityRule.False, null, Visibility.Visible)]
        public void BoolRules_ShowOrCollapse(VisibilityRule rule, bool? value, Visibility expected) =>
            Assert.Equal(expected, Convert(new VisibilityConverter { Rule = rule }, value));

        [Theory]
        [InlineData(VisibilityRule.NotNull, "x", Visibility.Visible)]
        [InlineData(VisibilityRule.NotNull, null, Visibility.Collapsed)]
        [InlineData(VisibilityRule.Null, null, Visibility.Visible)]
        [InlineData(VisibilityRule.Null, "x", Visibility.Collapsed)]
        public void NullRules_ShowOrCollapse(VisibilityRule rule, string? value, Visibility expected) =>
            Assert.Equal(expected, Convert(new VisibilityConverter { Rule = rule }, value));

        [Theory]
        [InlineData(VisibilityRule.NotEmpty, "text", Visibility.Visible)]
        [InlineData(VisibilityRule.NotEmpty, "", Visibility.Collapsed)]
        [InlineData(VisibilityRule.NotEmpty, null, Visibility.Collapsed)]
        public void NotEmpty_IsAboutText(VisibilityRule rule, string? value, Visibility expected) =>
            Assert.Equal(expected, Convert(new VisibilityConverter { Rule = rule }, value));

        [Fact]
        public void ItemRules_CountCollectionsAndPlainSequences()
        {
            var has = new VisibilityConverter { Rule = VisibilityRule.HasItems };
            var none = new VisibilityConverter { Rule = VisibilityRule.NoItems };
            static IEnumerable<int> Sequence(int n) { for (var i = 0; i < n; i++) yield return i; }

            Assert.Equal(Visibility.Visible, Convert(has, new List<int> { 1 }));
            Assert.Equal(Visibility.Collapsed, Convert(has, new List<int>()));
            Assert.Equal(Visibility.Collapsed, Convert(has, null));
            Assert.Equal(Visibility.Visible, Convert(has, Sequence(2)));
            Assert.Equal(Visibility.Collapsed, Convert(has, Sequence(0)));
            Assert.Equal(Visibility.Visible, Convert(none, new List<int>()));
            Assert.Equal(Visibility.Visible, Convert(none, null));
            Assert.Equal(Visibility.Collapsed, Convert(none, new[] { 1, 2 }));
        }

        [Fact]
        public void WhenHidden_CanBeHiddenInsteadOfCollapsed() =>
            Assert.Equal(Visibility.Hidden, Convert(new VisibilityConverter { Rule = VisibilityRule.True, WhenHidden = Visibility.Hidden }, false));

        [Fact]
        public void BoolTarget_GetsABool_ForTriggersAndIsEnabled() =>
            Assert.Equal(true, new VisibilityConverter { Rule = VisibilityRule.HasItems }.Convert(new[] { 1 }, typeof(bool), null!, CultureInfo.InvariantCulture));

        [Fact]
        public void ConvertBack_IsNotSupported() =>
            Assert.Throws<System.NotSupportedException>(() => new VisibilityConverter().ConvertBack(Visibility.Visible, typeof(bool), null!, CultureInfo.InvariantCulture));

        [Theory]
        [InlineData(1400.0, 1400.0, true)]
        [InlineData(1920.0, 1400.0, true)]
        [InlineData(1024.0, 1400.0, false)]
        [InlineData(0.0, 1400.0, false)]
        public void WidthAtLeast_ComparesAgainstTheParameter(double width, double threshold, bool wide)
        {
            var c = new WidthAtLeastConverter();
            Assert.Equal(wide ? Visibility.Visible : Visibility.Collapsed, c.Convert(width, typeof(Visibility), threshold, CultureInfo.InvariantCulture));
            Assert.Equal(wide, c.Convert(width, typeof(bool), threshold, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void WidthAtLeast_AcceptsATextParameter_AndIgnoresNonNumbers()
        {
            var c = new WidthAtLeastConverter();
            Assert.Equal(Visibility.Visible, c.Convert(1500.0, typeof(Visibility), "1400", CultureInfo.InvariantCulture));
            Assert.Equal(Visibility.Collapsed, c.Convert("wide", typeof(Visibility), 1400.0, CultureInfo.InvariantCulture));
            Assert.Equal(Visibility.Collapsed, c.Convert(1500.0, typeof(Visibility), null!, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void BoolToGridLength_PicksTheLengthByTheBool()
        {
            var c = new BoolToGridLengthConverter { TrueLength = GridLength.Auto, FalseLength = new GridLength(2, GridUnitType.Star) };
            Assert.Equal(GridLength.Auto, c.Convert(true, typeof(GridLength), null!, CultureInfo.InvariantCulture));
            Assert.Equal(new GridLength(2, GridUnitType.Star), c.Convert(false, typeof(GridLength), null!, CultureInfo.InvariantCulture));
            Assert.Equal(new GridLength(2, GridUnitType.Star), c.Convert(null!, typeof(GridLength), null!, CultureInfo.InvariantCulture));
        }

        [Theory]
        [InlineData("Auto;2*", true, "Auto")]
        [InlineData("Auto;2*", false, "2*")]
        [InlineData("0;*", true, "0")]
        [InlineData("120;3.4*", false, "3.4*")]
        public void BoolToGridLength_ParameterOverridesBothLengths(string parameter, bool value, string expected)
        {
            var c = new BoolToGridLengthConverter();
            var actual = (GridLength)c.Convert(value, typeof(GridLength), parameter, CultureInfo.InvariantCulture);
            Assert.Equal(new GridLengthConverter().ConvertFromInvariantString(expected), actual);
        }

        [Fact]
        public void BoolToGridLength_ABadParameterFallsBackToTheProperties()
        {
            var c = new BoolToGridLengthConverter { TrueLength = new GridLength(10), FalseLength = new GridLength(20) };
            Assert.Equal(new GridLength(10), c.Convert(true, typeof(GridLength), "nonsense", CultureInfo.InvariantCulture));
            Assert.Equal(new GridLength(20), c.Convert(false, typeof(GridLength), "only-one", CultureInfo.InvariantCulture));
        }
    }
}
