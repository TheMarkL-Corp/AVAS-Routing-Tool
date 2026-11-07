using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AvasRoutingTool.Rtsp
{
    /// <summary>
    /// Copies BGRA frames into a <see cref="WriteableBitmap"/>, recreating it when the frame size changes.
    /// Has no LibVLC dependency. <see cref="Present"/> must run on the thread that owns the bitmap (the UI thread).
    /// </summary>
    internal sealed class RtspFrameSink
    {
        /// <summary>Frames narrower or shorter than this are treated as garbage and ignored.</summary>
        internal const int MinSide = 16;

        public WriteableBitmap? Bitmap { get; private set; }

        /// <returns>true when the bitmap now holds this frame.</returns>
        public bool Present(int width, int height, IntPtr pixels, int stride)
        {
            if (width < MinSide || height < MinSide || pixels == IntPtr.Zero || stride < width * 4) return false;
            try
            {
                if (Bitmap == null || Bitmap.PixelWidth != width || Bitmap.PixelHeight != height)
                    Bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                Bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride * height, stride);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
