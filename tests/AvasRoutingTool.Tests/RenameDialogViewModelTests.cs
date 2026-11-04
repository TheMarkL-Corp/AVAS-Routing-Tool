using System.Collections.Generic;
using System.ComponentModel;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>The rename dialog's logic (plan R4): the typed name, the one-line reason, whether Rename is allowed, the counter.</summary>
    public class RenameDialogViewModelTests
    {
        private static RenameDialogViewModel Make(string current = "TX2_DUAL LINK", params string[] others) =>
            new(current, others);

        [Fact]
        public void Opens_WithTheCurrentName_NothingToRename_AndTheUnchangedReason()
        {
            var vm = Make("TX2_DUAL LINK");

            Assert.Equal("TX2_DUAL LINK", vm.CurrentName);
            Assert.Equal("TX2_DUAL LINK", vm.Name);
            Assert.False(vm.CanRename);
            Assert.Equal("Type a different name", vm.Reason);
            Assert.True(vm.HasReason);
        }

        [Fact]
        public void Title_NamesTheDevice()
        {
            Assert.Equal("Rename TX2_DUAL LINK", Make("TX2_DUAL LINK").Title);
        }

        [Fact]
        public void ValidNewName_CanRename_AndHasNoReason()
        {
            var vm = Make("TX2");
            vm.Name = "Stage left";

            Assert.True(vm.CanRename);
            Assert.Equal(string.Empty, vm.Reason);
            Assert.False(vm.HasReason);
        }

        [Theory]
        [InlineData("", "Enter a name")]
        [InlineData("12345678901234567890", "At most 19 characters (it has 20)")]
        [InlineData(" Stage", "No space at the start or end")]
        [InlineData("Stage ", "No space at the start or end")]
        [InlineData("Stage:left", "A colon (:) is not allowed")]
        public void InvalidName_ShowsTheNameRulesReason_AndCannotRename(string typed, string reason)
        {
            var vm = Make("TX2");
            vm.Name = typed;

            Assert.False(vm.CanRename);
            Assert.Equal(reason, vm.Reason);
            Assert.True(vm.HasReason);
        }

        [Fact]
        public void NameOfAnotherDevice_IsRefused_IgnoringCase()
        {
            var vm = Make("TX2", "Lobby TX", "RX1");
            vm.Name = "lobby tx";

            Assert.False(vm.CanRename);
            Assert.Equal("Another device already has this name", vm.Reason);
        }

        [Fact]
        public void TypingTheCurrentNameAgain_IsUnchanged_NotAnError()
        {
            var vm = Make("TX2");
            vm.Name = "Other";
            Assert.True(vm.CanRename);

            vm.Name = "TX2";

            Assert.False(vm.CanRename);
            Assert.Equal("Type a different name", vm.Reason);
        }

        [Fact]
        public void CountText_IsTheLengthOverTheServerLimit()
        {
            var vm = Make("TX2");
            Assert.Equal("3 / 19", vm.CountText);

            vm.Name = "Stage left 1";
            Assert.Equal("12 / 19", vm.CountText);

            vm.Name = "";
            Assert.Equal("0 / 19", vm.CountText);
        }

        [Fact]
        public void EveryDerivedProperty_RaisesPropertyChanged_WhenTheNameChanges()
        {
            var vm = Make("TX2");
            var raised = new List<string?>();
            ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Name = "Stage left";

            Assert.Contains(nameof(RenameDialogViewModel.Name), raised);
            Assert.Contains(nameof(RenameDialogViewModel.Reason), raised);
            Assert.Contains(nameof(RenameDialogViewModel.HasReason), raised);
            Assert.Contains(nameof(RenameDialogViewModel.CanRename), raised);
            Assert.Contains(nameof(RenameDialogViewModel.CountText), raised);
        }

        [Fact]
        public void IsError_IsTrueForABrokenRule_ButNotForTheUnchangedHint()
        {
            var vm = Make("TX2");
            Assert.False(vm.IsError); // "Type a different name" is a hint

            vm.Name = "";
            Assert.True(vm.IsError);

            vm.Name = "Stage left";
            Assert.False(vm.IsError);
        }

        [Fact]
        public void NullName_IsTreatedAsEmpty()
        {
            var vm = Make("TX2");
            vm.Name = null!;

            Assert.Equal(string.Empty, vm.Name);
            Assert.False(vm.CanRename);
            Assert.Equal("Enter a name", vm.Reason);
        }
    }
}
