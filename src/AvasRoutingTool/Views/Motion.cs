using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AvasRoutingTool.Views
{
    /// <summary>
    /// The only places the feature animates or announces. Durations come from the view models (<c>MotionPolicy</c>):
    /// zero means Windows animations are off, and the element goes straight to its end state.
    /// </summary>
    internal static class Motion
    {
        /// <summary>
        /// Fades the element in (to full opacity). A zero <paramref name="duration"/> starts no animation and clears a
        /// running one, so the element is shown in full at once.
        /// </summary>
        public static void FadeIn(UIElement element, TimeSpan duration, double from = 0)
        {
            if (duration <= TimeSpan.Zero)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                return;
            }
            element.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(from, 1, new Duration(duration)) { FillBehavior = FillBehavior.Stop });
        }

        /// <summary>
        /// Tells assistive technology that a live region's text changed (WPF does not raise this by itself when a
        /// bound text changes). Does nothing while no screen reader listens.
        /// </summary>
        public static void Announce(UIElement element)
        {
            if (!AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;

            // After the binding has put the new text in: the screen reader reads the region when it is told.
            element.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                UIElementAutomationPeer.CreatePeerForElement(element)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged)));
        }
    }
}
