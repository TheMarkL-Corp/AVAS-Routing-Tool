using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    /// <summary>
    /// Confirms a link switch (D-A3): names the transmitter and the target mode, lists the displays that are routed to it
    /// and the multi-view layouts that use it, and offers "Unroute and switch" or "Cancel". Nothing is changed until the
    /// operator confirms.
    /// </summary>
    public partial class LinkSwitchDialog : Window
    {
        public LinkSwitchDialog(LinkSwitchRequest request)
        {
            InitializeComponent();

            string mode = request.Target == LinkMode.Dual ? "dual" : "single";
            Title = $"Switch {request.DeviceName} to {mode} link?";
            HeadingText.Text = Title;

            var lines = new List<string>
            {
                $"{request.DeviceName} reboots to change its link mode and its picture is lost for a while."
            };
            if (request.Impact.MultiViewDisplays.Any())
                lines.Add($"Taken out of the multi-view on: {string.Join(", ", request.Impact.MultiViewDisplays)}.");
            if (request.Impact.SingleViewDisplays.Any())
                lines.Add($"Routed to it in Single View (the route stays, the picture drops while it reboots): {string.Join(", ", request.Impact.SingleViewDisplays)}.");
            if (!request.Impact.HasAny)
                lines.Add("Nothing is routed from it.");
            lines.Add("Nothing is routed again automatically.");
            AffectedText.Text = string.Join("\n\n", lines);

            ConfirmButton.Content = request.Impact.MultiViewDisplays.Any() ? "Unroute and switch" : "Switch";
        }

        private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
