using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using AvasRoutingTool.Services;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace AvasRoutingTool.Tests
{
    /// <summary>UI v2 rename dialog (plan R4 part A): 48 px field and buttons, the reason line, Enter / Esc, both themes.</summary>
    public partial class ViewSmokeTests
    {
        private static readonly string[] RenameOthers = { "Lobby TX", "Wall RX" };

        /// <summary>A shown dialog (Show, not ShowDialog: a test cannot block) with its view model, laid out.</summary>
        private static void ShowingRenameDialog(Action<RenameDialog, RenameDialogViewModel> assert, string current = "TX2_DUAL LINK")
        {
            var vm = new RenameDialogViewModel(current, RenameOthers);
            var dialog = new RenameDialog(vm) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = 0 };
            dialog.Show();
            try
            {
                dialog.UpdateLayout();
                Idle();
                dialog.UpdateLayout();
                assert(dialog, vm);
            }
            finally
            {
                dialog.Close();
            }
        }

        private static TextBox NameBoxOf(RenameDialog d) => Assert.IsType<TextBox>(d.FindName("NameBox"));
        private static Button RenameButtonOf(RenameDialog d) => Assert.IsType<Button>(d.FindName("RenameButton"));
        private static Button CancelButtonOf(RenameDialog d) => Assert.IsType<Button>(d.FindName("CancelButton"));
        private static TextBlock ReasonTextOf(RenameDialog d) => Assert.IsType<TextBlock>(d.FindName("ReasonText"));

        [Fact]
        public void RenameDialog_NameBox_IsTouchSized_Named_AndHasNoMaxLength() =>
            RunSta(() => ShowingRenameDialog((d, _) =>
            {
                var box = NameBoxOf(d);
                Assert.Same(Application.Current.FindResource("Style.TextBox.Field"), box.Style);
                Assert.True(box.ActualHeight >= 48, $"NameBox is {box.ActualHeight}px high");
                Assert.Equal("New name", AutomationProperties.GetName(box));
                Assert.Equal(0, box.MaxLength); // the user must be able to see why a long name is refused
                Assert.True(box.FontSize >= 12);
            }));

        [Fact]
        public void RenameDialog_Buttons_AreTouchSized_TabStops_AndTheEnterAndEscapeButtons() =>
            RunSta(() => ShowingRenameDialog((d, _) =>
            {
                var rename = RenameButtonOf(d);
                var cancel = CancelButtonOf(d);
                Assert.Equal("Rename", rename.Content);
                Assert.Equal("Cancel", cancel.Content);
                Assert.True(rename.ActualHeight >= 48, $"Rename is {rename.ActualHeight}px high");
                Assert.True(cancel.ActualHeight >= 48, $"Cancel is {cancel.ActualHeight}px high");
                Assert.True(rename.IsTabStop);
                Assert.True(cancel.IsTabStop);
                Assert.True(rename.IsDefault);
                Assert.False(rename.IsCancel);
                Assert.True(cancel.IsCancel);
                Assert.False(cancel.IsDefault);
            }));

        [Fact]
        public void RenameDialog_ShowsTheTitle_AndTheOneLineExplanation() =>
            RunSta(() => ShowingRenameDialog((d, _) =>
            {
                AssertVisibleText(d, "Rename TX2_DUAL LINK");
                AssertVisibleText(d, "The new name is written to the Control Server.");
            }));

        [Fact]
        public void RenameDialog_Opens_WithTheNameSelected_AndFocusedInTheField() =>
            RunSta(() => ShowingRenameDialog((d, _) =>
            {
                var box = NameBoxOf(d);
                Assert.Equal("TX2_DUAL LINK", box.Text);
                Assert.Equal(box.Text.Length, box.SelectionLength);
                Assert.Same(box, FocusManager.GetFocusedElement(d));
            }));

        [Fact]
        public void RenameDialog_UnchangedName_DisablesRename_AndSaysWhy() =>
            RunSta(() => ShowingRenameDialog((d, _) =>
            {
                Assert.False(RenameButtonOf(d).IsEnabled);
                var reason = ReasonTextOf(d);
                Assert.True(reason.IsVisible);
                Assert.Equal("Type a different name", reason.Text);
            }));

        [Theory]
        [InlineData("", "Enter a name")]
        [InlineData("12345678901234567890", "At most 19 characters (it has 20)")]
        [InlineData("Stage:left", "A colon (:) is not allowed")]
        [InlineData("lobby tx", "Another device already has this name")]
        public void RenameDialog_InvalidName_DisablesRename_AndShowsTheReason(string typed, string reason) =>
            RunSta(() => ShowingRenameDialog((d, _) =>
            {
                var box = NameBoxOf(d);
                box.Text = typed; // typing: the two-way binding updates the view model
                d.UpdateLayout();
                Idle();

                Assert.False(RenameButtonOf(d).IsEnabled);
                var line = ReasonTextOf(d);
                Assert.True(line.IsVisible);
                Assert.Equal(reason, line.Text);
            }));

        [Fact]
        public void RenameDialog_LongName_IsAccepted_ByTheField_SoTheReasonCanSayWhy() =>
            RunSta(() => ShowingRenameDialog((d, vm) =>
            {
                var box = NameBoxOf(d);
                box.Text = new string('x', 40);
                Assert.Equal(40, box.Text.Length);
                Assert.Equal(40, vm.Name.Length);
                Assert.Equal("40 / 19", Assert.IsType<TextBlock>(d.FindName("CountText")).Text);
            }));

        [Fact]
        public void RenameDialog_ValidNewName_EnablesRename_HidesTheReason_AndShowsTheCount() =>
            RunSta(() => ShowingRenameDialog((d, _) =>
            {
                NameBoxOf(d).Text = "Stage left";
                d.UpdateLayout();
                Idle();

                Assert.True(RenameButtonOf(d).IsEnabled);
                Assert.False(ReasonTextOf(d).IsVisible);
                Assert.Equal("10 / 19", Assert.IsType<TextBlock>(d.FindName("CountText")).Text);
            }));

        [Fact]
        public void RenameDialog_ClickingRename_ConfirmsTheTypedName() =>
            RunSta(() => ShowingRenameDialog((d, _) =>
            {
                NameBoxOf(d).Text = "Stage left";
                var rename = RenameButtonOf(d);
                Assert.True(rename.IsEnabled);

                bool closed = false;
                d.Closed += (_, _) => closed = true;
                rename.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                Assert.True(closed);
                Assert.Equal("Stage left", d.NewName);
            }));

        [Fact]
        public void RenameDialog_ClickingCancel_ClosesWithoutAName() =>
            RunSta(() => ShowingRenameDialog((d, _) =>
            {
                NameBoxOf(d).Text = "Stage left";
                bool closed = false;
                d.Closed += (_, _) => closed = true;
                CancelButtonOf(d).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                Assert.True(closed);
                Assert.False(d.Confirmed);
            }));

        [Fact]
        public void RenameDialog_AllVisibleText_IsAtLeast12px() =>
            RunSta(() => ShowingRenameDialog((d, _) =>
            {
                var texts = FindAll<TextBlock>(d).Where(t => t.IsVisible && !string.IsNullOrEmpty(t.Text)).ToList();
                Assert.NotEmpty(texts);
                foreach (var t in texts) Assert.True(t.FontSize >= 12, $"'{t.Text}' is {t.FontSize}px");
            }));

        [Theory]
        [InlineData(ThemeMode.Light)]
        [InlineData(ThemeMode.Dark)]
        public void RenameDialog_RendersInBothThemes(ThemeMode mode) =>
            RunSta(() =>
            {
                var theme = new ThemeService();
                try
                {
                    var vm = new RenameDialogViewModel("TX2_DUAL LINK", RenameOthers);
                    var dialog = new RenameDialog(vm) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = 0 };
                    theme.SetTheme(mode); // after building the view model and window, as the other themed smokes do
                    dialog.Show();
                    try
                    {
                        dialog.UpdateLayout();
                        Idle();
                        Assert.True(dialog.ActualWidth > 0 && dialog.ActualHeight > 0);
                        Assert.True(NameBoxOf(dialog).ActualHeight >= 48);
                        Assert.True(ReasonTextOf(dialog).IsVisible); // "Type a different name"
                        Assert.False(RenameButtonOf(dialog).IsEnabled);
                    }
                    finally
                    {
                        dialog.Close();
                    }
                }
                finally
                {
                    theme.SetTheme(ThemeMode.Dark);
                }
            });
    }
}
