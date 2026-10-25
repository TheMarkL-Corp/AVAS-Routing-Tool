using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class DisplayCardViewModelTests
    {
        [Fact]
        public void UnrouteCommand_WhenExecuted_CallsUnrouteActionAndClearsState()
        {
            var device = new AvasDevice
            {
                DeviceId = "74fe483c059e",
                DeviceName = "RX1_c059e_SFPA",
                IsReceiver = true,
                ActiveSourceId = "74fe488b07bb",
                ActiveSourceName = "TX2_DUAL LINK_SFPA"
            };

            bool unrouteCalled = false;
            var vm = new DisplayCardViewModel(device, d =>
            {
                unrouteCalled = true;
                d.ActiveSourceId = string.Empty;
                d.ActiveSourceName = string.Empty;
                return Task.CompletedTask;
            });

            Assert.True(vm.IsRouted);
            Assert.Equal("TX2_DUAL LINK_SFPA", vm.ActiveSourceName);

            vm.UnrouteCommand.Execute(null);

            Assert.True(unrouteCalled);
            Assert.False(vm.IsRouted);
            Assert.Empty(vm.ActiveSourceName);
            Assert.Null(vm.PreviewBitmap);
        }

        [Fact]
        public void SetPreviewSource_WhenAvasSource_MirrorsPreviewBitmapAndSetsSupported()
        {
            var rxDevice = new AvasDevice
            {
                DeviceId = "74fe483c059e",
                DeviceName = "RX1_c059e_SFPA",
                IsReceiver = true
            };
            var rxVm = new DisplayCardViewModel(rxDevice);

            var txDevice = new AvasDevice
            {
                DeviceId = "74fe488b07bb",
                DeviceName = "TX2_DUAL LINK_SFPA",
                VendorId = 105,
                IsTransmitter = true
            };
            var txVm = new SourceCardViewModel(txDevice);

            rxVm.SetPreviewSource(txVm);

            Assert.True(rxVm.IsPreviewSupported);

            // When unrouted
            rxVm.SetPreviewSource(null);
            Assert.False(rxVm.IsPreviewSupported);
            Assert.Null(rxVm.PreviewBitmap);
        }

        [Fact]
        public void SetPreviewSource_WhenNonAvasSource_SetsPreviewNotSupported()
        {
            var rxDevice = new AvasDevice
            {
                DeviceId = "74fe483c059e",
                DeviceName = "RX1_c059e_SFPA",
                IsReceiver = true
            };
            var rxVm = new DisplayCardViewModel(rxDevice);

            var txDevice = new AvasDevice
            {
                DeviceId = "0016c0589c1e",
                DeviceName = "VEGA_1100",
                VendorId = 192,
                IsTransmitter = true
            };
            var txVm = new SourceCardViewModel(txDevice);

            rxVm.SetPreviewSource(txVm);

            Assert.False(rxVm.IsPreviewSupported);
            Assert.Null(rxVm.PreviewBitmap);
        }

        [Fact]
        public void UnrouteCommand_CanExecute_FollowsIsRoutedState()
        {
            var device = new AvasDevice
            {
                DeviceId = "74fe483c059e",
                DeviceName = "RX1_c059e_SFPA",
                IsReceiver = true,
                ActiveSourceId = ""
            };
            var vm = new DisplayCardViewModel(device);

            Assert.False(vm.IsRouted);
            Assert.False(vm.UnrouteCommand.CanExecute(null));

            device.ActiveSourceId = "74fe488b07bb";
            device.ActiveSourceName = "TX2_DUAL LINK_SFPA";
            vm.NotifyRoutingChanged();

            Assert.True(vm.IsRouted);
            Assert.True(vm.UnrouteCommand.CanExecute(null));
        }

        [Fact]
        public void SetPreviewSource_WhenSourcePreviewBitmapChanges_PropagatesToDisplayCard()
        {
            var rxDevice = new AvasDevice { DeviceId = "74fe483c059e", DeviceName = "RX1", IsReceiver = true };
            var rxVm = new DisplayCardViewModel(rxDevice);

            var txDevice = new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = "TX2", VendorId = 105, IsTransmitter = true };
            var txVm = new SourceCardViewModel(txDevice);

            rxVm.SetPreviewSource(txVm);

            bool displayBitmapChanged = false;
            rxVm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DisplayCardViewModel.PreviewBitmap))
                {
                    displayBitmapChanged = true;
                }
            };

            // Simulate source preview bitmap assignment
            var dummyBitmap = new WriteableBitmap(16, 16, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null);
            txVm.PreviewBitmap = dummyBitmap;

            Assert.True(displayBitmapChanged);
            Assert.Same(dummyBitmap, rxVm.PreviewBitmap);
        }
    }
}
