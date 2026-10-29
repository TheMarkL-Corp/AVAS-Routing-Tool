using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AvasRoutingTool.Converters
{
    /// <summary>What a bound value must be for the element to show.</summary>
    public enum VisibilityRule
    {
        /// <summary>The value is <c>true</c>.</summary>
        True,
        /// <summary>The value is not <c>true</c> (false or null).</summary>
        False,
        NotNull,
        Null,
        /// <summary>A non-empty string.</summary>
        NotEmpty,
        /// <summary>A collection or sequence with at least one item.</summary>
        HasItems,
        /// <summary>No collection, or one with no items.</summary>
        NoItems,
    }

    /// <summary>
    /// One converter for every "show when ..." binding, so views need no inline <c>DataTrigger</c> style for plain visibility.
    /// Instances for each rule live in <c>Resources/Converters.xaml</c> (<c>Converter.Visible.&lt;Rule&gt;</c>).
    /// A bool target (a trigger, <c>IsEnabled</c>) gets the plain answer instead of a <see cref="Visibility"/>.
    /// </summary>
    public sealed class VisibilityConverter : IValueConverter
    {
        public VisibilityRule Rule { get; set; } = VisibilityRule.True;

        /// <summary>What a failing rule gives: <see cref="Visibility.Collapsed"/> (no room kept) or <see cref="Visibility.Hidden"/>.</summary>
        public Visibility WhenHidden { get; set; } = Visibility.Collapsed;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var show = Rule switch
            {
                VisibilityRule.True => value is true,
                VisibilityRule.False => value is not true,
                VisibilityRule.NotNull => value != null,
                VisibilityRule.Null => value == null,
                VisibilityRule.NotEmpty => value is string s && s.Length > 0,
                VisibilityRule.HasItems => CountsAny(value),
                VisibilityRule.NoItems => !CountsAny(value),
                _ => false,
            };
            return targetType == typeof(bool) ? show : show ? Visibility.Visible : WhenHidden;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();

        private static bool CountsAny(object? value)
        {
            if (value is ICollection collection) return collection.Count > 0;
            if (value is string or null) return false;
            return value is IEnumerable sequence && sequence.GetEnumerator().MoveNext();
        }
    }
}
