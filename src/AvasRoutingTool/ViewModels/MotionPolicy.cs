using System;
using System.Windows;

namespace AvasRoutingTool.ViewModels
{
    /// <summary>
    /// Whether the interface may animate (spec 9, 10: "skipped when Windows animations are off"). The setting is read
    /// whenever a duration is asked for, so a change in Windows takes effect at once. Every transition the views run
    /// takes its duration from here; zero means "go straight to the end state".
    /// </summary>
    public sealed class MotionPolicy
    {
        /// <summary>The fade of a toast or banner appearing (spec 9: 150 ms fades).</summary>
        public static readonly TimeSpan FadeTime = TimeSpan.FromMilliseconds(150);

        /// <summary>The staged frame turning into the live frame after a Take (spec 8: one 400 ms transition).</summary>
        public static readonly TimeSpan FrameTransitionTime = TimeSpan.FromMilliseconds(400);

        private readonly Func<bool> _animationsEnabled;

        /// <param name="animationsEnabled">The animation setting; default <see cref="SystemParameters.ClientAreaAnimation"/> (Windows "Show animations").</param>
        public MotionPolicy(Func<bool>? animationsEnabled = null)
        {
            _animationsEnabled = animationsEnabled ?? (() => SystemParameters.ClientAreaAnimation);
        }

        /// <summary>Windows animations are on: transitions may run.</summary>
        public bool AnimationsEnabled => _animationsEnabled();

        /// <summary><paramref name="normal"/> with animations on; <see cref="TimeSpan.Zero"/> (skip it) with animations off.</summary>
        public TimeSpan Resolve(TimeSpan normal) => AnimationsEnabled ? normal : TimeSpan.Zero;

        public TimeSpan Fade => Resolve(FadeTime);

        public TimeSpan FrameTransition => Resolve(FrameTransitionTime);
    }
}
