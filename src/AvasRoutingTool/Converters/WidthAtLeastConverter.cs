using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AvasRoutingTool.Converters
{
    /// <summary>
    /// A width (an element's <c>ActualWidth</c>) against the converter parameter, usually a <c>Size.Breakpoint.*</c> token:
    /// <see cref="Visibility.Visible"/> (or <c>true</c> for a bool target) when it is at least that wide.
    /// Used to show the app-bar labels only on a wide window without code-behind.
    /// </summary>
    public sealed class WidthAtLeastConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var wide = value is double width && TryNumber(parameter, out var limit) && width >= limit;
            return targetType == typeof(bool) ? wide : wide ? Visibility.Visible : Visibility.Collapsed;
        }

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
