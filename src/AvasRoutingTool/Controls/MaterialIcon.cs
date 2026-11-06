using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AvasRoutingTool.Controls
{
    /// <summary>
    /// A Google Material Symbols Rounded icon (weight 400, outline). <see cref="Kind"/> is the Material name (e.g. <c>play_arrow</c>);
    /// the shape is the <c>MIcon.&lt;Kind&gt;</c> Geometry in <c>Resources/MaterialIcons.xaml</c>, drawn in a 24 x 24 box and scaled to
    /// Width x Height (default <c>Size.Icon.M</c>, from the implicit Style in MaterialIcons.xaml, so XAML values and style setters override it). It is tinted by <see cref="Control.Foreground"/> (inherited, so a button's text colour
    /// and its disabled dimming apply). An unknown Kind throws <see cref="InvalidOperationException"/> naming the Kind (never a blank icon).
    /// It is decorative: not hit-testable, not focusable, and not exposed to UI Automation unless <c>AutomationProperties.Name</c> is set.
    /// </summary>
    public class MaterialIcon : Control
    {
        public const string PathPartName = "PART_Path";

        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(string), typeof(MaterialIcon), new FrameworkPropertyMetadata(string.Empty, OnKindChanged));

        private static readonly ControlTemplate IconTemplate = BuildTemplate();

        static MaterialIcon()
        {
            IsHitTestVisibleProperty.OverrideMetadata(typeof(MaterialIcon), new FrameworkPropertyMetadata(false));
            FocusableProperty.OverrideMetadata(typeof(MaterialIcon), new FrameworkPropertyMetadata(false));
            IsTabStopProperty.OverrideMetadata(typeof(MaterialIcon), new FrameworkPropertyMetadata(false));
        }

        public MaterialIcon()
        {
            Template = IconTemplate;
        }

        /// <summary>The Material Symbols name, e.g. <c>settings</c>, <c>play_arrow</c>, <c>expand_more</c>.</summary>
        public string Kind
        {
            get => (string)GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }

        private static ControlTemplate BuildTemplate()
        {
            var path = new FrameworkElementFactory(typeof(Path));
            path.Name = PathPartName;
            path.SetValue(Shape.FillProperty, new TemplateBindingExtension(ForegroundProperty));
            path.SetValue(Shape.StrokeProperty, null);

            var box = new FrameworkElementFactory(typeof(Canvas));
            box.SetValue(WidthProperty, 24.0);
            box.SetValue(HeightProperty, 24.0);
            box.AppendChild(path);

            var view = new FrameworkElementFactory(typeof(Viewbox));
            view.SetValue(Viewbox.StretchProperty, Stretch.Uniform);
            view.AppendChild(box);

            var template = new ControlTemplate(typeof(MaterialIcon)) { VisualTree = view };
            template.Seal();
            return template;
        }

        private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var icon = (MaterialIcon)d;
            if (icon.GetTemplateChild(PathPartName) is Path) icon.ApplyGeometry();
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            ApplyGeometry();
        }

        private void ApplyGeometry()
        {
            if (GetTemplateChild(PathPartName) is not Path path) return;
            var kind = Kind;
            if (string.IsNullOrWhiteSpace(kind)) { path.Data = null; return; }
            var key = "MIcon." + kind;
            path.Data = TryFindResource(key) as Geometry
                ?? throw new InvalidOperationException($"MaterialIcon Kind '{kind}' has no Geometry resource '{key}'. Add the name to tools/icons/import-material-icons.ps1 and run it.");
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new MaterialIconAutomationPeer(this);

        private sealed class MaterialIconAutomationPeer : FrameworkElementAutomationPeer
        {
            public MaterialIconAutomationPeer(MaterialIcon owner) : base(owner) { }

            private bool IsNamed => !string.IsNullOrEmpty(AutomationProperties.GetName(Owner));

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
            protected override string GetClassNameCore() => nameof(MaterialIcon);
            protected override bool IsControlElementCore() => IsNamed;
            protected override bool IsContentElementCore() => IsNamed;
        }
    }
}
