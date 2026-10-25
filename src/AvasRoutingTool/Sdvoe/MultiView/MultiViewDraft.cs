using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <summary>A source staged in one block. <see cref="SlotIndex"/> is the API window index of the block.</summary>
    public sealed record DraftPlacement(int SlotIndex, string SourceMac, string SourceName);

    /// <summary>Outcome of a staging request; <see cref="Reason"/> is operator-readable text when it was refused.</summary>
    public sealed record DraftResult(bool Ok, string? Reason)
    {
        public static DraftResult Success { get; } = new(true, null);

        public static DraftResult Refuse(string reason) => new(false, reason);
    }

    /// <summary>One block that differs from what is on screen. Names, not MACs; null means the block is empty.</summary>
    public sealed record DraftChange(int SlotIndex, string? From, string? To);

    /// <summary>
    /// What the operator is arranging for one display, staged until Take. Pure state: nothing here touches a
    /// device. A source may fill several blocks only when they are the same pixel size, because one encoder
    /// has one scaler size per layout.
    /// </summary>
    public sealed class MultiViewDraft
    {
        private readonly Dictionary<int, DraftPlacement> _placements = new();
        private readonly List<DraftPlacement> _unplaced = new();

        public MultiViewDraft(MultiViewLayoutPlan plan)
        {
            Plan = plan;
            Placements = new ReadOnlyDictionary<int, DraftPlacement>(_placements);
        }

        public MultiViewLayoutPlan Plan { get; }

        /// <summary>Staged sources keyed by block (API window index).</summary>
        public IReadOnlyDictionary<int, DraftPlacement> Placements { get; }

        /// <summary>Sources a layout change left without a block, in the order they were placed.</summary>
        public IReadOnlyList<DraftPlacement> Unplaced => _unplaced;

        public DraftResult Place(int slot, string mac, string name)
        {
            if (SlotAt(slot) == null) return DraftResult.Refuse("This layout has no such block.");

            var trial = new Dictionary<int, DraftPlacement>(_placements)
            {
                [slot] = new DraftPlacement(slot, mac, name),
            };
            string? conflict = SizeConflict(trial, slot);
            if (conflict != null) return DraftResult.Refuse(conflict);

            _placements[slot] = trial[slot];
            return DraftResult.Success;
        }

        public void Clear(int slot) => _placements.Remove(slot);

        public void ClearAll() => _placements.Clear();

        /// <summary>Moves a source to another block; an occupied target swaps with it.</summary>
        public DraftResult Move(int from, int to)
        {
            if (SlotAt(from) == null || SlotAt(to) == null) return DraftResult.Refuse("This layout has no such block.");
            if (!_placements.TryGetValue(from, out var moving))
                return DraftResult.Refuse($"Block {BlockNumber(from)} is empty.");
            if (from == to) return DraftResult.Success;

            var trial = new Dictionary<int, DraftPlacement>(_placements);
            trial.Remove(from);
            if (_placements.TryGetValue(to, out var displaced))
                trial[from] = displaced with { SlotIndex = from };
            trial[to] = moving with { SlotIndex = to };

            string? conflict = SizeConflict(trial, to) ?? (trial.ContainsKey(from) ? SizeConflict(trial, from) : null);
            if (conflict != null) return DraftResult.Refuse(conflict);

            _placements.Clear();
            foreach (var pair in trial) _placements[pair.Key] = pair.Value;
            return DraftResult.Success;
        }

        /// <summary>
        /// The same sources on another layout: the old logical order maps onto the new one (first block to
        /// first block...). Anything with no block left, or whose block would give its source a second size,
        /// goes to <see cref="Unplaced"/>. This draft is left untouched.
        /// </summary>
        public MultiViewDraft WithLayout(MultiViewLayoutPlan plan)
        {
            var next = new MultiViewDraft(plan);
            next._unplaced.AddRange(_unplaced);

            for (int i = 0; i < Plan.Slots.Count; i++)
            {
                if (!_placements.TryGetValue(Plan.Slots[i].Index, out var placement)) continue;

                if (i < plan.Slots.Count)
                {
                    int target = plan.Slots[i].Index;
                    var moved = placement with { SlotIndex = target };
                    var trial = new Dictionary<int, DraftPlacement>(next._placements) { [target] = moved };
                    if (next.SizeConflict(trial, target) == null)
                    {
                        next._placements[target] = moved;
                        continue;
                    }
                }

                next._unplaced.Add(placement);
            }

            return next;
        }

        /// <summary>Blocks whose source differs from <paramref name="live"/> (null = nothing on screen), in block order.</summary>
        public IReadOnlyList<DraftChange> DiffFrom(MultiViewDraft? live)
        {
            var liveBlocks = live?.Placements ?? new Dictionary<int, DraftPlacement>();
            var order = Plan.Slots.Select(s => s.Index)
                .Concat(liveBlocks.Keys.Where(k => SlotAt(k) == null).OrderBy(k => k));

            var changes = new List<DraftChange>();
            foreach (int slot in order)
            {
                _placements.TryGetValue(slot, out var staged);
                liveBlocks.TryGetValue(slot, out var onScreen);

                string? stagedMac = staged == null ? null : SdvoeClient.NormalizeMac(staged.SourceMac);
                string? liveMac = onScreen == null ? null : SdvoeClient.NormalizeMac(onScreen.SourceMac);
                if (stagedMac != liveMac) changes.Add(new DraftChange(slot, onScreen?.SourceName, staged?.SourceName));
            }
            return changes;
        }

        public MultiViewDraft Clone()
        {
            var copy = new MultiViewDraft(Plan);
            foreach (var pair in _placements) copy._placements[pair.Key] = pair.Value;
            copy._unplaced.AddRange(_unplaced);
            return copy;
        }

        /// <summary>Blocks showing the source, in block order.</summary>
        public IEnumerable<int> SlotsShowing(string mac)
        {
            string norm = SdvoeClient.NormalizeMac(mac);
            return Plan.Slots
                .Where(s => _placements.TryGetValue(s.Index, out var p) && SdvoeClient.NormalizeMac(p.SourceMac) == norm)
                .Select(s => s.Index)
                .ToList();
        }

        private WindowSlot? SlotAt(int index) => Plan.Slots.FirstOrDefault(s => s.Index == index);

        /// <summary>1-based position in logical order - the number the operator sees.</summary>
        private int BlockNumber(int index) => Plan.Slots.ToList().FindIndex(s => s.Index == index) + 1;

        /// <summary>Refusal text when the source at <paramref name="slot"/> also fills a block of another size, else null.</summary>
        private string? SizeConflict(IReadOnlyDictionary<int, DraftPlacement> layout, int slot)
        {
            var candidate = layout[slot];
            var here = SlotAt(slot)!;
            string norm = SdvoeClient.NormalizeMac(candidate.SourceMac);

            foreach (var other in Plan.Slots)
            {
                if (other.Index == slot || !layout.TryGetValue(other.Index, out var existing)) continue;
                if (SdvoeClient.NormalizeMac(existing.SourceMac) != norm) continue;
                if (other.Width == here.Width && other.Height == here.Height) continue;

                return $"{candidate.SourceName} is in block {BlockNumber(other.Index)} at {other.Width}x{other.Height}" +
                       $" - this block is {here.Width}x{here.Height}. One encoder has one size per layout.";
            }
            return null;
        }
    }
}
