using System.Windows.Controls;
using System.Windows.Input;

namespace AvasRoutingTool.Views
{
    /// <summary>A source in the Composer's compact palette strip: tap arms, drag places (same input as <see cref="SourceCardView"/>).</summary>
    public partial class CompactSourceCardView : UserControl
    {
        private readonly TapGesture _gesture = new();

        public CompactSourceCardView()
        {
            InitializeComponent();
        }

        private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => SourceCardInput.Down(_gesture, this, e);

        private void Card_MouseMove(object sender, MouseEventArgs e) => SourceCardInput.Move(_gesture, this, e);

        private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => SourceCardInput.Up(_gesture, this);
    }
}
