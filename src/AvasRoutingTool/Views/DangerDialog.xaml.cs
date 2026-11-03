using System;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    /// <summary>
    /// Confirms a destructive action from the Danger zone. The title names the device; the confirm button is never the primary
    /// one and Cancel is the escape. When the request asks for a typed name, the confirm button stays disabled until the text
    /// matches the device name exactly (case and spaces count). Nothing happens until the operator confirms.
    /// </summary>
    public partial class DangerDialog : Window
    {
        private readonly string? _typedName;

        public DangerDialog(DangerRequest request)
        {
            InitializeComponent();

            Title = request.Title;
            HeadingText.Text = request.Title;
            BodyText.Text = request.Body;
            ConfirmButton.Content = request.ConfirmLabel;

            _typedName = request.TypedName;
            if (_typedName != null)
            {
                TypedPrompt.Text = $"Type {_typedName} to confirm.";
                TypedPrompt.Visibility = Visibility.Visible;
                TypedNameBox.Visibility = Visibility.Visible;
                ConfirmButton.IsEnabled = false;
                Loaded += (_, _) => TypedNameBox.Focus();
            }
        }

        private void TypedNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ConfirmButton.IsEnabled = _typedName == null || string.Equals(TypedNameBox.Text, _typedName, StringComparison.Ordinal);
        }

        private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
