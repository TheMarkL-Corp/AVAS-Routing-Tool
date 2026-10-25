using System.Linq;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class DisplayCardMultiViewTests
    {
        private static DisplayCardViewModel NewDisplay() =>
            new(new AvasDevice { DeviceId = "74fe483c059e", DeviceName = "RX1", IsReceiver = true, IsActive = true });

        private static SourceCardViewModel NewSource(string mac = "74fe488b07bb", int vendor = 105) =>
            new(new AvasDevice { DeviceId = mac, DeviceName = "TX " + mac, VendorId = vendor, IsTransmitter = true, IsActive = true });

        private static MultiViewLayoutPlan Plan(string id) => MultiViewPresets.Build(id, 1920, 1080);

        [Fact]
        public void NewDisplay_IsSingleView()
        {
            var vm = NewDisplay();

            Assert.Equal("single", vm.LayoutId);
            Assert.False(vm.IsMultiView);
            Assert.Empty(vm.Slots);
            Assert.Equal("UNROUTED", vm.ModeBadge);
        }

        [Fact]
        public void ApplyLayout_BuildsOneEmptySlotPerWindowInPaintOrder()
        {
            var vm = NewDisplay();

            vm.ApplyLayout(Plan("pip"), "PIP");

            Assert.True(vm.IsMultiView);
            Assert.Equal("pip", vm.LayoutId);
            Assert.Equal(new[] { 1, 0 }, vm.Slots.Select(s => s.Index).ToArray()); // back to front
            Assert.All(vm.Slots, s => Assert.False(s.IsAssigned));
            Assert.Equal("PIP · 0/2", vm.ModeBadge); // layout and fill: no block carries a source yet
            Assert.False(vm.IsRouted);
        }

        [Fact]
        public void ApplyLayout_SlotsCarryNormalisedRectsForThePanel()
        {
            var vm = NewDisplay();

            vm.ApplyLayout(Plan("matrix2x2"), "MATRIX 2X2");

            var last = vm.Slots.Single(s => s.Index == 3);
            Assert.Equal(0.5, last.NormalizedX, 3);
            Assert.Equal(0.5, last.NormalizedY, 3);
            Assert.Equal(0.5, last.NormalizedWidth, 3);
            Assert.Equal(0.5, last.NormalizedHeight, 3);
            Assert.Equal("4", last.Label);
        }

        [Fact]
        public void AssignSlot_ShowsSourceNameMirrorsBitmapAndMarksRouted()
        {
            var vm = NewDisplay();
            vm.ApplyLayout(Plan("matrix2x2"), "MATRIX 2X2");
            var src = NewSource();
            var bmp = new WriteableBitmap(16, 16, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null);
            src.PreviewBitmap = bmp;

            vm.AssignSlot(1, src);

            var slot = vm.Slots.Single(s => s.Index == 1);
            Assert.True(slot.IsAssigned);
            Assert.Equal(src.DeviceName, slot.SourceName);
            Assert.Same(bmp, slot.PreviewBitmap);
            Assert.True(slot.IsPreviewSupported);
            Assert.True(vm.IsRouted);
            Assert.Equal("1 IN LAYOUT", vm.SourceLabel);
        }

        [Fact]
        public void AssignSlot_FollowsLaterBitmapChanges()
        {
            var vm = NewDisplay();
            vm.ApplyLayout(Plan("matrix2x2"), "MATRIX 2X2");
            var src = NewSource();
            vm.AssignSlot(0, src);

            var bmp = new WriteableBitmap(16, 16, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null);
            src.PreviewBitmap = bmp;

            Assert.Same(bmp, vm.Slots.Single(s => s.Index == 0).PreviewBitmap);
        }

        [Fact]
        public void AssignSlot_NonAvasSource_HasNoPreview()
        {
            var vm = NewDisplay();
            vm.ApplyLayout(Plan("matrix2x2"), "MATRIX 2X2");

            vm.AssignSlot(2, NewSource("0016c0589c1e", vendor: 192));

            var slot = vm.Slots.Single(s => s.Index == 2);
            Assert.True(slot.IsAssigned);
            Assert.False(slot.IsPreviewSupported);
            Assert.Null(slot.PreviewBitmap);
        }

        [Fact]
        public void ClearSlot_EmptiesOnlyThatSlotAndStopsMirroring()
        {
            var vm = NewDisplay();
            vm.ApplyLayout(Plan("matrix2x2"), "MATRIX 2X2");
            var src = NewSource();
            vm.AssignSlot(0, src);
            vm.AssignSlot(1, NewSource("74fe488b07bc"));

            vm.ClearSlot(0);
            src.PreviewBitmap = new WriteableBitmap(16, 16, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null);

            var cleared = vm.Slots.Single(s => s.Index == 0);
            Assert.False(cleared.IsAssigned);
            Assert.Null(cleared.PreviewBitmap);
            Assert.True(vm.Slots.Single(s => s.Index == 1).IsAssigned);
            Assert.Equal("1 IN LAYOUT", vm.SourceLabel);
        }

        [Fact]
        public void ClearLayout_ReturnsToSingleViewAndDropsSlots()
        {
            var vm = NewDisplay();
            vm.ApplyLayout(Plan("matrix2x2"), "MATRIX 2X2");
            vm.AssignSlot(0, NewSource());

            vm.ClearLayout();

            Assert.Equal("single", vm.LayoutId);
            Assert.False(vm.IsMultiView);
            Assert.Empty(vm.Slots);
            Assert.False(vm.IsRouted);
            Assert.Equal("UNROUTED", vm.ModeBadge);
        }

        [Fact]
        public void FirstEmptySlotIndex_SkipsAssignedSlotsInLogicalOrder()
        {
            var vm = NewDisplay();
            vm.ApplyLayout(Plan("matrix2x2"), "MATRIX 2X2");
            Assert.Equal(0, vm.FirstEmptySlotIndex);

            vm.AssignSlot(0, NewSource());
            Assert.Equal(1, vm.FirstEmptySlotIndex);

            vm.AssignSlot(1, NewSource("a"));
            vm.AssignSlot(2, NewSource("b"));
            vm.AssignSlot(3, NewSource("c"));
            Assert.Null(vm.FirstEmptySlotIndex);
        }

        [Fact]
        public void ModeBadge_SingleViewRouted_IsSingleView()
        {
            var vm = NewDisplay();
            vm.Device.ActiveSourceId = "74fe488b07bb";
            vm.Device.ActiveSourceName = "TX2";
            vm.NotifyRoutingChanged();

            Assert.Equal("SINGLE VIEW", vm.ModeBadge);
        }

        [Fact]
        public void ModeBadge_FollowsTheFill_AndIsRaised()
        {
            var vm = NewDisplay();
            vm.ApplyLayout(Plan("side_by_side"), "Side-by-Side");
            bool raised = false;
            vm.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(DisplayCardViewModel.ModeBadge);

            vm.AssignSlot(1, NewSource());

            Assert.True(raised);
            Assert.Equal("SIDE-BY-SIDE · 1/2", vm.ModeBadge);
        }

        [Fact]
        public void DropChoice_OpensWithTheSource_AndCloses()
        {
            var vm = NewDisplay();
            var src = NewSource();

            vm.OpenDropChoice(src);
            Assert.True(vm.IsDropChoiceOpen);
            Assert.Same(src, vm.DropChoiceSource);

            vm.CloseDropChoice();
            Assert.False(vm.IsDropChoiceOpen);
            Assert.Null(vm.DropChoiceSource);
        }

        [Fact]
        public void LayoutMenu_MarksTheLayoutOnScreen()
        {
            var vm = NewDisplay();
            Assert.True(vm.LayoutMenu.Single(l => l.Id == "single").IsSelected);

            vm.ApplyLayout(Plan("pip"), "PIP");

            Assert.True(vm.LayoutMenu.Single(l => l.Id == "pip").IsSelected);
            Assert.False(vm.LayoutMenu.Single(l => l.Id == "single").IsSelected);
            Assert.All(vm.LayoutMenu, l => Assert.True(l.IsAvailable));
        }

        [Fact]
        public void LastError_RaisesPropertyChanged()
        {
            var vm = NewDisplay();
            bool raised = false;
            vm.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(DisplayCardViewModel.LastError);

            vm.LastError = "boom";

            Assert.True(raised);
            Assert.True(vm.HasError);
        }
    }
}
