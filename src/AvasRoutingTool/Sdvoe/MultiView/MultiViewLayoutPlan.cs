using System.Collections.Generic;
using System.Linq;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <summary>A multiview layout ready to be sent to the Control Server and drawn in the UI.</summary>
    public sealed class MultiViewLayoutPlan
    {
        public MultiViewLayoutPlan(string id, string name, int width, int height, IReadOnlyList<WindowSlot> slots)
        {
            Id = id;
            Name = name;
            Width = width;
            Height = height;
            Slots = slots;
        }

        /// <summary>Preset id, e.g. <c>matrix2x2</c>.</summary>
        public string Id { get; }

        /// <summary>Layout name on the Control Server, e.g. <c>avas_matrix2x2_1920x1080</c>.</summary>
        public string Name { get; }

        public int Width { get; }
        public int Height { get; }

        /// <summary>Windows in logical order (the order operators think of them).</summary>
        public IReadOnlyList<WindowSlot> Slots { get; }

        /// <summary>Windows back-to-front: highest API index first, so the lowest index is painted on top.</summary>
        public IEnumerable<WindowSlot> PaintOrder => Slots.OrderByDescending(s => s.Index);
    }
}
