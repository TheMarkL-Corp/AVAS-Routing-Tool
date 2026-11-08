using System.Windows;

namespace AvasRoutingTool.Views
{
    /// <summary>
    /// Whether a Composer block is small (a PIP inset, a 2x2 cell on a small window). Set once on the block root and inherited
    /// by every overlay in it, so compact sizes are ordinary style triggers instead of per-element setters.
    /// </summary>
    public static class BlockDensity
    {
        /// <summary>A block narrower than this (px) gets compact overlays. A layout-algorithm constant, like GridSpec.</summary>
        public const double CompactBelow = 240;

        public static readonly DependencyProperty IsCompactProperty = DependencyProperty.RegisterAttached(
            "IsCompact", typeof(bool), typeof(BlockDensity),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        public static bool GetIsCompact(DependencyObject d) => (bool)d.GetValue(IsCompactProperty);

        public static void SetIsCompact(DependencyObject d, bool value) => d.SetValue(IsCompactProperty, value);
    }
}
