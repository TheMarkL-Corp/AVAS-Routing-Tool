using System.Linq;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// UI v2 display card action bar strings (plan task A10): the inline preset names and tooltips, and the Copy layout tooltip.
    /// The view tests read these; the wording is decided here.
    /// </summary>
    public class DisplayCardInlineCopyTests
    {
        private const string ComposerHelp = "Opens the Composer. Nothing changes on screen until Take.";

        [Theory]
        [InlineData("single", "Single view")]
        [InlineData("pip", "Picture-in-picture")]
        [InlineData("side_by_side", "Side by side")]
        [InlineData("matrix2x2", "Quad")]
        public void InlineName_IsTheShortNameOfEachInlinePreset_AndTheTooltipIsTheSameName(string id, string name)
        {
            var item = LayoutItemViewModel.CreateCatalog().Single(l => l.Id == id);
            Assert.Equal(name, item.InlineName);
            Assert.Equal(name, item.InlineToolTip);
        }

        [Fact]
        public void InlineName_OfAnotherLayout_IsItsTitle()
        {
            var item = LayoutItemViewModel.CreateCatalog().Single(l => l.Id == "pip_large");
            Assert.Equal("PIP Large", item.InlineName);
            Assert.Equal(item.Title, item.InlineToolTip);
        }

        [Theory]
        [InlineData("single", "")]
        [InlineData("pip", ComposerHelp)]
        [InlineData("side_by_side", ComposerHelp)]
        [InlineData("matrix2x2", ComposerHelp)]
        public void InlineHelpText_SaysThatAMultiViewPresetOpensTheComposer(string id, string help)
        {
            var item = LayoutItemViewModel.CreateCatalog().Single(l => l.Id == id);
            Assert.Equal(help, item.InlineHelpText);
        }

        [Fact]
        public void CopyHandleToolTip_SaysWhatCopyDoes_OrWhyThereIsNothingToCopy()
        {
            var device = new AvasDevice { DeviceId = "74fe483c059e", DeviceName = "RX1_c059e_SFPA", IsReceiver = true, IsActive = true };
            var vm = new DisplayCardViewModel(device);
            Assert.False(vm.CanCopyFrom);
            Assert.Equal("No layout to copy yet", vm.CopyHandleToolTip);
            // The reason stays in the help text and the automation name; the tooltip only says what is off.
            Assert.Equal("RX1_c059e_SFPA shows nothing to copy", vm.CopyFromBlockedText);

            device.ActiveSourceId = "74fe488b07bb";
            device.ActiveSourceName = "TX2_DUAL LINK_SFPA";
            vm.NotifyRoutingChanged();

            Assert.True(vm.CanCopyFrom);
            Assert.Equal("Copy this layout to another display", vm.CopyHandleToolTip);
        }
    }
}
