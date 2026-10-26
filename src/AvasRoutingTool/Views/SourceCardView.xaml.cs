using System.Windows.Controls;
using System.Windows.Input;

namespace AvasRoutingTool.Views
{
    public partial class SourceCardView : UserControl
    {
        private readonly TapGesture _gesture = new();

        public SourceCardView()
        {
            InitializeComponent();
        }

        private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => SourceCardInput.Down(_gesture, this, e);

        private void Card_MouseMove(object sender, MouseEventArgs e) => SourceCardInput.Move(_gesture, this, e);

        /// <summary>Enter or Space arms the focused source (the keyboard path of a tap).</summary>
        private void Card_KeyDown(object sender, KeyEventArgs e) => SourceCardInput.KeyDown(this, e);

        /// <summary>A tap (no drag) arms the source; see <see cref="SourceCardInput.Up"/>.</summary>
        private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => SourceCardInput.Up(_gesture, this);
    }
}
