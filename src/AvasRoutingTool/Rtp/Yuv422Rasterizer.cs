using System;
using System.Runtime.CompilerServices;

namespace AvasRoutingTool.Rtp
{
    /// <summary>
    /// YUV 4:2:2 to RGB24 / BGR24 rasterizer for the RFC 4175 "YCbCr-4:2:2" 8-bit pixel group
    /// (byte order Cb Y0 Cr Y1, a.k.a. UYVY), limited range (Y 16-235, C 16-240), BT.709 matrix.
    /// Q8 fixed-point integer arithmetic; all converters share one pixel routine so they cannot drift apart.
    /// Verified byte order and range against a raw AVAS-223 preview frame (see Handover/03_PROTOCOLS_AND_HARDWARE.md section 5.3).
    /// </summary>
    public static class Yuv422Rasterizer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Clamp(int val) => (byte)(val < 0 ? 0 : (val > 255 ? 255 : val));

        /// <summary>Converts one luma sample with the pair's chroma (already offset by -128) to R, G, B.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Pixel(int y, int d, int e, out byte r, out byte g, out byte b)
        {
            int c = 298 * (y - 16) + 128;
            r = Clamp((c + 459 * e) >> 8);
            g = Clamp((c - 55 * d - 136 * e) >> 8);
            b = Clamp((c + 541 * d) >> 8);
        }

        public static byte[] ConvertYuv422ToRgb24(ReadOnlySpan<byte> yuv, int width, int height)
        {
            byte[] rgb = new byte[width * height * 3];
            ConvertYuv422ToRgb24(yuv, rgb.AsSpan(), width, height);
            return rgb;
        }

        public static void ConvertYuv422ToRgb24(ReadOnlySpan<byte> yuv, Span<byte> rgb, int width, int height)
        {
            int s = 0, o = 0;
            while (s <= yuv.Length - 4 && o <= rgb.Length - 6)
            {
                int d = yuv[s] - 128, e = yuv[s + 2] - 128;
                Pixel(yuv[s + 1], d, e, out rgb[o], out rgb[o + 1], out rgb[o + 2]);
                Pixel(yuv[s + 3], d, e, out rgb[o + 3], out rgb[o + 4], out rgb[o + 5]);
                s += 4;
                o += 6;
            }
        }

        public static byte[] ConvertYuv422ToBgr24(ReadOnlySpan<byte> yuv, int width, int height)
        {
            byte[] bgr = new byte[width * height * 3];
            ConvertYuv422ToBgr24(yuv, bgr.AsSpan(), width, height);
            return bgr;
        }

        public static void ConvertYuv422ToBgr24(ReadOnlySpan<byte> yuv, Span<byte> bgr, int width, int height)
        {
            int s = 0, o = 0;
            while (s <= yuv.Length - 4 && o <= bgr.Length - 6)
            {
                int d = yuv[s] - 128, e = yuv[s + 2] - 128;
                Pixel(yuv[s + 1], d, e, out bgr[o + 2], out bgr[o + 1], out bgr[o]);
                Pixel(yuv[s + 3], d, e, out bgr[o + 5], out bgr[o + 4], out bgr[o + 3]);
                s += 4;
                o += 6;
            }
        }

        public static unsafe void ConvertYuv422ToBgr24(byte* pYuv, byte* pBgr, int pixelCount)
        {
            int pairs = pixelCount >> 1;
            for (int i = 0; i < pairs; i++)
            {
                int d = pYuv[0] - 128, e = pYuv[2] - 128;
                Pixel(pYuv[1], d, e, out pBgr[2], out pBgr[1], out pBgr[0]);
                Pixel(pYuv[3], d, e, out pBgr[5], out pBgr[4], out pBgr[3]);
                pYuv += 4;
                pBgr += 6;
            }
        }

        public static unsafe void ConvertYuv422ToBgr24Stride(byte* pYuv, byte* pBackBuffer, int width, int height, int stride)
        {
            int yuvLineBytes = width * 2;
            for (int line = 0; line < height; line++)
            {
                ConvertYuv422ToBgr24(pYuv + (line * yuvLineBytes), pBackBuffer + (line * stride), width);
            }
        }
    }
}
