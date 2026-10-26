using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    /// <summary>(width, height) of the staged plan -> aspect ratio for <see cref="Controls.FractionalLayoutPanel"/>; 16:9 until a layout is chosen.</summary>
    public sealed class AspectRatioConverter : IMultiValueConverter
    {
        public const double Default = 16.0 / 9.0;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] is int w && values[1] is int h && w > 0 && h > 0) return (double)w / h;
            return Default;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// API window index -> Panel.ZIndex: the lowest index is painted on top, as the decoder paints it (the PIP inset
    /// is window 0 and comes first in logical order, so the order of the items alone would hide it).
    /// </summary>
    public sealed class PaintOrderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is int index ? -index : 0;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>True when the value is null (lets a trigger react to "something is set" without a second property).</summary>
    public sealed class IsNullConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value == null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>True when the number is below the parameter (a block narrower than 240 px gets compact overlays).</summary>
    public sealed class LessThanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double v && double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double limit) &&
            v > 0 && v < limit;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>-value / 2: centres an element on a zero-size Canvas (an overlay that may overhang its block unclipped).</summary>
    public sealed class NegativeHalfConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double v ? -v / 2 : 0.0;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>True when both values are the same non-empty text, ignoring case (e.g. a block's source MAC and the armed source's).</summary>
    public sealed class SameTextConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            values.Length == 2 && values[0] is string a && values[1] is string b && a.Length > 0 &&
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>True when the collection has at least one item (works for arrays, whose Count is not a public property).</summary>
    public sealed class HasItemsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
        {
            ICollection c => c.Count > 0,
            IEnumerable e => e.Cast<object>().Any(),
            _ => false,
        };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>Finds the overview's <see cref="MainViewModel"/> from inside a view (an ancestor's DataContext, else the main window's).</summary>
    internal static class ViewLookup
    {
        public static MainViewModel? FindMainViewModel(DependencyObject? from)
        {
            for (var d = from; d != null; d = Parent(d))
            {
                if (d is FrameworkElement { DataContext: MainViewModel vm }) return vm;
            }
            return Application.Current?.MainWindow?.DataContext as MainViewModel;
        }

        private static DependencyObject? Parent(DependencyObject d) =>
            d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
    }
}
