using System;

namespace AvasRoutingTool.Rtsp
{
    /// <summary>Retry waits: 1, 2, 5 s, then 10 s forever. attempt is 1-based.</summary>
    public static class RtspRetryPolicy
    {
        public static TimeSpan NextDelay(int attempt) => attempt switch
        {
            <= 1 => TimeSpan.FromSeconds(1),
            2 => TimeSpan.FromSeconds(2),
            3 => TimeSpan.FromSeconds(5),
            _ => TimeSpan.FromSeconds(10),
        };
    }
}
