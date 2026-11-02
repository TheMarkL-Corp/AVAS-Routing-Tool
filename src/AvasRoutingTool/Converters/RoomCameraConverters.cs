using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace AvasRoutingTool.Converters
{
    /// <summary>
    /// A bound value (usually an enum) compared with the converter parameter by name: <c>true</c> (or <see cref="Visibility.Visible"/>
    /// for a Visibility target) when they match. Back: a checked radio/toggle writes the named enum value; unchecking writes nothing.
    /// </summary>
    public sealed class ValueEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var same = value != null && parameter != null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);
            return targetType == typeof(Visibility) ? (same ? Visibility.Visible : Visibility.Collapsed) : same;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (value is true && parameter != null && type.IsEnum && Enum.TryParse(type, parameter.ToString(), out var result))
                return result!;
            return Binding.DoNothing;
        }
    }

    /// <summary>True when the first bound value is the same object as the second (a chip is the selected camera).</summary>
    public sealed class SameReferenceConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            values.Length == 2 && values[0] != null && ReferenceEquals(values[0], values[1]);

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            targetTypes.Select(_ => Binding.DoNothing).ToArray();
    }

    /// <summary>The short link word shown in a camera chip, from the full link text ("Cannot reach 1.2.3.4:5" becomes "Unreachable").</summary>
    public sealed class LinkWordConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string ?? string.Empty;
            if (text.StartsWith("Connected", StringComparison.Ordinal)) return "Connected";
            if (text.StartsWith("Connecting", StringComparison.Ordinal)) return "Connecting";
            if (text.StartsWith("Cannot", StringComparison.Ordinal)) return "Unreachable";
            return "Retrying";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
