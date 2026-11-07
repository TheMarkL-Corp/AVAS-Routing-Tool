using System.Windows;

namespace AvasRoutingTool.Controls
{
    /// <summary>Carries a DataContext into places outside the visual tree (a CompositeCollection's CollectionContainer).</summary>
    public sealed class BindingProxy : Freezable
    {
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));

        public object? Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        protected override Freezable CreateInstanceCore() => new BindingProxy();
    }
}
