namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <summary>
    /// Whether, and how, a display's picture may be copied to another display. Allowed text is the summary
    /// plus an optional note; blocked text is the reason. <see cref="Reason"/> is null when allowed,
    /// <see cref="Note"/> is null when blocked.
    /// </summary>
    public sealed record CopyDecision(bool Allowed, string Summary, string? Note, string? Reason);

    /// <summary>
    /// Pure copy rule (spec section 15): the target's native size (preferred EDID timing) must be at least the
    /// source's output in width and in height, compared separately, and its EDID must list the source's exact mode.
    /// </summary>
    public static class DisplayCopyPlanner
    {
        public static CopyDecision Decide(
            string sourceName, ScreenState source, string targetName, bool targetOnline, bool sameDisplay, EdidInfo? targetEdid)
        {
            if (sameDisplay) return Blocked(string.Empty, "Drop on another display");
            if (source.Kind == ScreenKind.Nothing) return Blocked(string.Empty, $"{sourceName} shows nothing to copy");

            string summary = $"Copy {sourceName} - {source.Describe()}";

            if (!targetOnline) return Blocked(summary, $"{targetName} is offline");

            var native = targetEdid?.Native;
            if (targetEdid == null || native == null)
                return Blocked(summary, $"Can't read {targetName}'s resolution - copy blocked to be safe");

            if (native.Width < source.Width || native.Height < source.Height)
                return Blocked(summary,
                    $"{targetName} is {native.Width}x{native.Height} - {sourceName}'s picture is {source.Width}x{source.Height}. Copy needs a display at least that large.");

            if (!targetEdid.Supports(source.Width, source.Height, source.Fps))
                return Blocked(summary, $"{targetName} does not list {source.Width}x{source.Height} @ {source.Fps} in its EDID");

            string? note = native.Width == source.Width && native.Height == source.Height
                ? null
                : $"Runs at {source.Width}x{source.Height} @ {source.Fps} on this {native.Width}x{native.Height} display";
            return new CopyDecision(true, summary, note, null);
        }

        private static CopyDecision Blocked(string summary, string reason) => new(false, summary, null, reason);
    }
}
