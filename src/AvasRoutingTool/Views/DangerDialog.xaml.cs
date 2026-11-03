using System.Windows;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    /// <summary>
    /// Confirms a destructive action from the Danger zone. The title names the device; the confirm button is never the primary
    /// one and Cancel is the escape. Nothing happens until the operator confirms.
    /// </summary>
    public partial class DangerDialog : Window
    {
        public DangerDialog(DangerRequest request)
        {
            InitializeComponent();

            Title = request.Title;
            HeadingText.Text = request.Title;
            BodyText.Text = request.Body;
            ConfirmButton.Content = request.ConfirmLabel;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
