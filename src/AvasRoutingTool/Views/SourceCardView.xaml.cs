using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AvasRoutingTool.Views
{
    public partial class SourceCardView : UserControl
    {
        /// <summary>
        /// The compact density (the Composer palette): a 112 x 63 thumbnail, the name and the status word, no stream controls.
        /// False (the default) is the full tile. Visibility is bound, so no measure invalidation is needed.
        /// </summary>
        public static readonly DependencyProperty IsCompactProperty =
            DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(SourceCardView), new PropertyMetadata(false));

        private readonly TapGesture _gesture = new();

        public SourceCardView()
        {
            InitializeComponent();
        }

        public bool IsCompact
        {
            get => (bool)GetValue(IsCompactProperty);
            set => SetValue(IsCompactProperty, value);
        }

        /// <summary>A press is being tracked for a possible drag (tests: a press on one of the tile's buttons is not).</summary>
        internal bool IsPressTracked => _gesture.IsPressed;

        private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => SourceCardInput.Down(_gesture, this, e);

        private void Card_MouseMove(object sender, MouseEventArgs e) => SourceCardInput.Move(_gesture, this, e);

        /// <summary>Enter or Space arms the focused source (the keyboard path of a tap).</summary>
        private void Card_KeyDown(object sender, KeyEventArgs e) => SourceCardInput.KeyDown(this, e);

        /// <summary>A tap (no drag) arms the source; see <see cref="SourceCardInput.Up"/>.</summary>
        private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => SourceCardInput.Up(_gesture, this);
    }
}
