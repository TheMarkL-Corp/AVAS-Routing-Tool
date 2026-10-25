namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <summary>
    /// One multiview window: where it sits on the decoder's output canvas and which frame-buffer
    /// surface feeds it. <see cref="Index"/> is the API window index; per the reference a higher
    /// index is painted behind a lower one.
    /// </summary>
    public sealed record WindowSlot(
        int Index,
        string Label,
        int X,
        int Y,
        int Width,
        int Height,
        int TargetSurface,
        double NormalizedX,
        double NormalizedY,
        double NormalizedWidth,
        double NormalizedHeight);
}
