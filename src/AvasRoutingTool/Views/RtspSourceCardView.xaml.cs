using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AvasRoutingTool.Rtsp;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    /// <summary>
    /// The view-only RTSP tile. DataContext is the MainViewModel. The address typed here goes to the parser and
    /// to the view model only; it is not shown, logged or stored by this view.
    /// </summary>
    public partial class RtspSourceCardView : UserControl
    {
        public RtspSourceCardView()
        {
            InitializeComponent();
            Refresh();
        }

        private MainViewModel? Main => DataContext as MainViewModel;

        private void AddressBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

        private void Refresh()
        {
            var text = AddressBox.Text ?? string.Empty;
            AddressPlaceholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (text.Trim().Length == 0)
            {
                ReasonText.Text = string.Empty;
                StartAddButton.IsEnabled = false;
                return;
            }
            bool ok = RtspUrl.TryParse(text, out _, out var reason);
            ReasonText.Text = ok ? string.Empty : reason;
            StartAddButton.IsEnabled = ok;
        }

        private void AddressBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Submit();
        }

        private void StartAddButton_Click(object sender, RoutedEventArgs e) => Submit();

        private void Submit()
        {
            var main = Main;
            if (main == null) return;
            if (main.TrySetRtspUrl(AddressBox.Text ?? string.Empty, out var reason))
                AddressBox.Text = string.Empty;
            else
                ReasonText.Text = reason;
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e) => Main?.RemoveRtsp();
    }
}
