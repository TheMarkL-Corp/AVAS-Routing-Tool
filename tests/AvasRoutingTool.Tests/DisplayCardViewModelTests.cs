using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class DisplayCardViewModelTests
    {
        [Theory]
        [InlineData("", "")]
        [InlineData("OUT 3840x2160@60 (VP3268-4K)", "OUT 3840x2160@60 (VP3268-4K)")]
        [InlineData("OUT 3840x2160@60 (running) · sources at half frame rate (~6.0 Gbps instead of ~12.0)", "OUT 3840x2160@60 (running) · half rate")]
        [InlineData("OUT 3840x2160@60 · ~11.0 Gbps even at half frame rate (>10 Gbps recommended)", "OUT 3840x2160@60 · over bandwidth")]
        public void ShortenCanvasInfo_KeepsTheOutputFormatAndDropsTheBandwidthSentence(string full, string expected)
        {
            Assert.Equal(expected, DisplayCardViewModel.ShortenCanvasInfo(full));
        }

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
        public void SetSingleViewOutput_ShowsWhileRouted_AndIsGoneOnceTheDisplayIsUnrouted()
        {
            var device = new AvasDevice { DeviceId = "74fe483c059e", DeviceName = "RX1", IsReceiver = true, ActiveSourceId = "74fe488b07bb", ActiveSourceName = "TX2" };
            var vm = new DisplayCardViewModel(device, d => { d.ActiveSourceId = string.Empty; d.ActiveSourceName = string.Empty; return Task.CompletedTask; });
            var changed = new System.Collections.Generic.List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            Assert.Equal(string.Empty, vm.CanvasInfo);

            vm.SetSingleViewOutput("OUT 3840x2160@60 (VP3268-4K)");

            Assert.Equal("OUT 3840x2160@60 (VP3268-4K)", vm.CanvasInfo);
            Assert.Contains(nameof(DisplayCardViewModel.CanvasInfo), changed);

            vm.UnrouteCommand.Execute(null);
            Assert.Equal(string.Empty, vm.CanvasInfo); // an unrouted card shows no output line
        }

        [Fact]
        public void NotifyRoutingChanged_DropsASingleViewLineThatMayNoLongerDescribeTheRoute()
        {
            var device = new AvasDevice { DeviceId = "74fe483c059e", DeviceName = "RX1", IsReceiver = true, ActiveSourceId = "74fe488b07bb", ActiveSourceName = "TX2" };
            var vm = new DisplayCardViewModel(device, d => Task.CompletedTask);
            vm.SetSingleViewOutput("OUT 3840x2160@60");

            vm.NotifyRoutingChanged(); // a route made elsewhere (restore, copy, undo): the old format is not known to hold

            Assert.Equal(string.Empty, vm.CanvasInfo);
        }

        [Fact]
        public void TheSingleViewLine_IsNotTheLayoutCanvas()
        {
            var device = new AvasDevice { DeviceId = "74fe483c059e", DeviceName = "RX1", IsReceiver = true, ActiveSourceId = "74fe488b07bb", ActiveSourceName = "TX2" };
            var vm = new DisplayCardViewModel(device, d => Task.CompletedTask);

            vm.SetSingleViewOutput("OUT 1920x1080@60 (display EDID unavailable)");

            Assert.NotEqual(string.Empty, vm.CanvasInfo);
            Assert.Equal(string.Empty, vm.LayoutCanvasInfo); // what the Composer reuses: only a multiview / external canvas
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

        [Fact]
        public void ExternalLayoutText_ForALayoutCopiedHere_IsFriendly_AndKeepsTheRawNameForTheTooltip()
        {
            var vm = new DisplayCardViewModel(new AvasDevice { DeviceId = "74fe4800355a", DeviceName = "PAX355S_SFP A", IsReceiver = true, IsActive = true });

            vm.SetExternalLayout("avas_74fe4800355a_copy_3840x2160", new[] { "TX2", "VEGA_1100" }, "OUT 3840x2160@60");

            Assert.Equal("Copied layout (read-only): TX2, VEGA_1100", vm.ExternalLayoutText);
            Assert.Equal("avas_74fe4800355a_copy_3840x2160", vm.ExternalLayoutName);

            // A layout made elsewhere keeps its name in the text (the operator may know it from BAVM).
            vm.SetExternalLayout("compatibility_4k_2x2", new[] { "TX2" }, "OUT 3840x2160@60");
            Assert.Equal("Running layout 'compatibility_4k_2x2' set elsewhere (read-only): TX2", vm.ExternalLayoutText);
            Assert.Equal("compatibility_4k_2x2", vm.ExternalLayoutName);

            vm.ClearExternalLayout();
            Assert.Equal(string.Empty, vm.ExternalLayoutText);
            Assert.Equal(string.Empty, vm.ExternalLayoutName);
        }
    }
}
