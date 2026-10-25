using System.Collections.Generic;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <summary>
    /// CTA-861 Video Identification Codes: the short codes a display lists in its Video Data Block.
    /// Refresh is the nominal rate (59.94 Hz modes are listed as 60, 23.976 as 24, and so on).
    /// Only codes whose timing is certain are listed; anything else is unknown and skipped.
    /// </summary>
    public static class CtaVic
    {
        private static readonly Dictionary<int, EdidMode> Table = BuildTable();

        public static bool TryGet(int vic, out EdidMode mode) => Table.TryGetValue(vic, out mode!);

        private static Dictionary<int, EdidMode> BuildTable()
        {
            var t = new Dictionary<int, EdidMode>();
            void Add(int vic, int w, int h, int hz, bool interlaced = false) => t[vic] = new EdidMode(w, h, hz, interlaced);

            // Standard definition and 720p/1080 at 50/60.
            Add(1, 640, 480, 60);
            Add(2, 720, 480, 60);
            Add(3, 720, 480, 60);
            Add(4, 1280, 720, 60);
            Add(5, 1920, 1080, 60, true);
            Add(6, 720, 480, 60, true);
            Add(7, 720, 480, 60, true);
            Add(16, 1920, 1080, 60);
            Add(17, 720, 576, 50);
            Add(18, 720, 576, 50);
            Add(19, 1280, 720, 50);
            Add(20, 1920, 1080, 50, true);
            Add(21, 720, 576, 50, true);
            Add(22, 720, 576, 50, true);
            Add(31, 1920, 1080, 50);
            Add(32, 1920, 1080, 24);
            Add(33, 1920, 1080, 25);
            Add(34, 1920, 1080, 30);

            // 100/120 Hz HD.
            Add(40, 1920, 1080, 100, true);
            Add(41, 1280, 720, 100);
            Add(46, 1920, 1080, 120, true);
            Add(47, 1280, 720, 120);
            Add(60, 1280, 720, 24);
            Add(61, 1280, 720, 25);
            Add(62, 1280, 720, 30);
            Add(63, 1920, 1080, 120);
            Add(64, 1920, 1080, 100);

            // 4K UHD (16:9) and DCI 4K.
            Add(93, 3840, 2160, 24);
            Add(94, 3840, 2160, 25);
            Add(95, 3840, 2160, 30);
            Add(96, 3840, 2160, 50);
            Add(97, 3840, 2160, 60);
            Add(98, 4096, 2160, 24);
            Add(99, 4096, 2160, 25);
            Add(100, 4096, 2160, 30);
            Add(101, 4096, 2160, 50);
            Add(102, 4096, 2160, 60);
            Add(103, 3840, 2160, 24);
            Add(104, 3840, 2160, 25);
            Add(105, 3840, 2160, 30);
            Add(106, 3840, 2160, 50);
            Add(107, 3840, 2160, 60);
            Add(117, 3840, 2160, 100);
            Add(118, 3840, 2160, 120);

            return t;
        }
    }
}
