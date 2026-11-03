using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AvasRoutingTool.Sdvoe;

namespace AvasRoutingTool.ViewModels
{
    /// <summary>The Advanced page: one row per discovered device, rebuilt on every discovery refresh.</summary>
    public class AdvancedViewModel
    {
        public ObservableCollection<AdvancedDeviceRowViewModel> Rows { get; } = new();

        public void SetDevices(IEnumerable<AvasDevice> devices)
        {
            var ordered = devices.Where(d => d.IsTransmitter).OrderBy(d => d.DeviceName)
                .Concat(devices.Where(d => d.IsReceiver && !d.IsTransmitter).OrderBy(d => d.DeviceName));

            Rows.Clear();
            foreach (var d in ordered) Rows.Add(new AdvancedDeviceRowViewModel(d));
        }
    }
}
