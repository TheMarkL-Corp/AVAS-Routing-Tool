using System;
using System.Windows;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    /// <summary>
    /// Asks for a device's new name. Show it with <c>ShowDialog()</c>: true means Rename was pressed and
    /// <see cref="NewName"/> holds the typed name. The rules and the reason line live in <see cref="RenameDialogViewModel"/>.
    /// </summary>
    public partial class RenameDialog : Window
    {
        private readonly RenameDialogViewModel _viewModel;

        public RenameDialog(RenameDialogViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeComponent();
            DataContext = _viewModel;

            // The current name is selected so typing replaces it; the user can also edit it in place.
            ContentRendered += (_, _) =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            };
        }

        /// <summary>The typed name (as typed: the rules already refuse a space at either end).</summary>
        public string NewName => _viewModel.Name;

        /// <summary>Rename was pressed (with a valid, changed name).</summary>
        public bool Confirmed { get; private set; }

        private void BtnRename_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanRename) return;
            Confirmed = true;
            Finish(true);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Finish(false);

        /// <summary>Closes with a dialog result; a window shown without ShowDialog (a test) cannot have one and just closes.</summary>
        private void Finish(bool accepted)
        {
            try
            {
                DialogResult = accepted;
            }
            catch (InvalidOperationException)
            {
                Close();
            }
        }
    }
}
