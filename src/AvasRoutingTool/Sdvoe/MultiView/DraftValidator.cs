using System;
using System.Collections.Generic;
using System.Linq;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    public enum BlockIssueKind { SourceOffline, SizeConflictOtherDisplay }

    /// <summary>Why a staged block cannot be taken. <see cref="Message"/> is shown to the operator as is.</summary>
    public sealed record BlockIssue(int SlotIndex, BlockIssueKind Kind, string Message);

    /// <summary>A source as another display's layout (live or staged) uses it: which encoder, at what scaler size.</summary>
    public sealed record OtherDisplayUse(string DisplayName, string SourceMac, int Width, int Height, bool IsInset = false);

    /// <summary>
    /// Checks a staged draft against what the rest of the system is doing. One encoder has one scaler size,
    /// so it cannot feed a differently sized block here while another display uses it at another size.
    /// A PiP inset (on either side) is exempt: lab-observed (2026-10-30) that an inset at 640x360 runs on one display
    /// while the same encoder feeds a 1920x1080 block on another; the decoder scales the surface into the window.
    /// The scaler readback behind that is still unconfirmed.
    /// </summary>
    public static class DraftValidator
    {
        public static IReadOnlyList<BlockIssue> Validate(MultiViewDraft d, IEnumerable<OtherDisplayUse> others, Func<string, bool> isOnline)
        {
            var otherUses = others.ToList();
            var issues = new List<BlockIssue>();

            foreach (var slot in d.Plan.Slots)
            {
                if (!d.Placements.TryGetValue(slot.Index, out var placement)) continue;

                if (!isOnline(placement.SourceMac))
                    issues.Add(new BlockIssue(slot.Index, BlockIssueKind.SourceOffline, OfflineText(placement.SourceName)));

                string? conflict = OtherDisplayConflict(slot, placement.SourceMac, placement.SourceName, otherUses);
                if (conflict != null)
                    issues.Add(new BlockIssue(slot.Index, BlockIssueKind.SizeConflictOtherDisplay, conflict));
            }
            return issues;
        }

        /// <summary>The refusal the operator would get for placing the source, or Ok. Does not change the draft.</summary>
        public static DraftResult CanPlace(MultiViewDraft d, int slot, string mac, string name,
            IEnumerable<OtherDisplayUse> others, Func<string, bool> isOnline)
        {
            if (!isOnline(mac)) return DraftResult.Refuse(OfflineText(name));

            var inLayout = d.Clone().Place(slot, mac, name);
            if (!inLayout.Ok) return inLayout;

            var block = d.Plan.Slots.First(s => s.Index == slot);
            string? conflict = OtherDisplayConflict(block, mac, name, others);
            return conflict == null ? DraftResult.Success : DraftResult.Refuse(conflict);
        }

        private static string OfflineText(string name) => $"{name} is offline";

        private static string? OtherDisplayConflict(WindowSlot block, string mac, string name, IEnumerable<OtherDisplayUse> others)
        {
            if (block.Label == MultiViewPresets.InsetLabel) return null;

            string norm = SdvoeClient.NormalizeMac(mac);
            var use = others.FirstOrDefault(o =>
                SdvoeClient.NormalizeMac(o.SourceMac) == norm && !o.IsInset && (o.Width != block.Width || o.Height != block.Height));

            return use == null
                ? null
                : $"{name} runs at {use.Width}x{use.Height} on {use.DisplayName} - this block is {block.Width}x{block.Height}.";
        }
    }
}
