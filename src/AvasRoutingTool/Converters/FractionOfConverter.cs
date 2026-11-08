using System;
using System.Globalization;
using System.Windows.Data;

namespace AvasRoutingTool.Converters
{
    /// <summary>
    /// A length (an element's <c>ActualHeight</c>) times a fraction (the converter parameter, usually <c>Size.Strip.Max.Fraction</c>):
    /// a cap that stays relative to the window. A parameter or value it cannot use gives <see cref="double.PositiveInfinity"/> (no cap).
    /// Used for the Sources strip's <c>MaxHeight</c> without code-behind.
    /// </summary>
    public sealed class FractionOfConverter : IValueConverter
    {
        /// <summary>The smallest cap it gives (0 = none): a window-relative cap that never drops below what one whole row of content needs.</summary>
        public double Floor { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double length && TryNumber(parameter, out var fraction) ? Math.Max(Floor, length * fraction) : double.PositiveInfinity;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();

        private static bool TryNumber(object? parameter, out double number)
        {
            switch (parameter)
            {
                case double d: number = d; return true;
                case int i: number = i; return true;
                case string s: return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
                default: number = 0; return false;
            }
        }
    }
}
