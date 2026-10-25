using System.Windows;
using AvasRoutingTool.Sdvoe.MultiView;

namespace AvasRoutingTool.ViewModels
{
    /// <summary>
    /// What a "Copy layout" drag over a display card shows to mouse users (spec 15, mouse extras): the copy cursor
    /// on an allowed target, the no-drop cursor on a blocked one, and a tooltip repeating the card's text. The view's
    /// DragOver uses only these; the visible card text stays the primary explanation (touch has no hover).
    /// </summary>
    public static class CopyDragFeedback
    {
        public static DragDropEffects Effects(CopyDecision decision) =>
            decision is { Allowed: true } ? DragDropEffects.Copy : DragDropEffects.None;

        /// <summary>The reason when blocked; else the note (e.g. "Runs at 1920x1080 @ 60 ...") or the summary.</summary>
        public static string Tooltip(CopyDecision decision)
        {
            if (decision == null) return string.Empty;
            return decision.Allowed ? decision.Note ?? decision.Summary : decision.Reason ?? string.Empty;
        }
    }
}
