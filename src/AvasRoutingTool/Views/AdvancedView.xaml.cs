using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    public partial class AdvancedView : UserControl
    {
        public AdvancedView()
        {
            InitializeComponent();
            IsVisibleChanged += OnIsVisibleChanged;
            DataContextChanged += (_, e) =>
            {
                if (e.NewValue is AdvancedViewModel vm) vm.Confirm = ConfirmLinkSwitch;
            };
        }

        // Status is read when the tab is opened, not in the background.
        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true && DataContext is AdvancedViewModel vm && vm.RefreshCommand.CanExecute(null))
                vm.RefreshCommand.Execute(null);
        }

        private Task<bool> ConfirmLinkSwitch(LinkSwitchRequest request)
        {
            var dialog = new LinkSwitchDialog(request) { Owner = Window.GetWindow(this) };
            return Task.FromResult(dialog.ShowDialog() == true);
        }
    }
}
