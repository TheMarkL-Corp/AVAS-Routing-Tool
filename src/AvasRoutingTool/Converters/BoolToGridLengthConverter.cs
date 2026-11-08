using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AvasRoutingTool.Converters
{
    /// <summary>
    /// A bool picks a <see cref="GridLength"/> (a row or column that changes size with a view-model flag).
    /// The two lengths are properties; a converter parameter <c>"&lt;true&gt;;&lt;false&gt;"</c> (for example <c>Auto;2*</c>) overrides them
    /// for one binding, so a single instance serves every row.
    /// </summary>
    public sealed class BoolToGridLengthConverter : IValueConverter
    {
        private static readonly GridLengthConverter Parser = new();

        public GridLength TrueLength { get; set; } = new(1, GridUnitType.Star);

        public GridLength FalseLength { get; set; } = GridLength.Auto;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var on = value is true;
            if (parameter is string text && TryParse(text, out var lengths)) return on ? lengths.True : lengths.False;
            return on ? TrueLength : FalseLength;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();

        private static bool TryParse(string text, out (GridLength True, GridLength False) lengths)
        {
            lengths = default;
            var parts = text.Split(';');
            if (parts.Length != 2) return false;
            try
            {
                lengths = ((GridLength)Parser.ConvertFromInvariantString(parts[0].Trim())!, (GridLength)Parser.ConvertFromInvariantString(parts[1].Trim())!);
                return true;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException)
            {
                return false;
            }
        }
    }
}
