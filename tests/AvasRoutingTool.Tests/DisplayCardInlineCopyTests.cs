using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// UI v2 display card action bar strings (plan task A10): the Copy layout tooltip and its reason (the inline presets were removed, A13).
    /// The view tests read these; the wording is decided here.
    /// </summary>
    public class DisplayCardInlineCopyTests
    {
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
