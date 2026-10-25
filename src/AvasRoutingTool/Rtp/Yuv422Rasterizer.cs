using System;

namespace AvasRoutingTool.Rtp
{
    /// <summary>
    /// High-performance YUV 4:2:2 to RGB24 / BGR24 color space rasterizer.
    /// Implements fast Q10 fixed-point integer arithmetic and a 1024-byte branchless clamp LUT.
    /// Supports zero-allocation span-based and unsafe pointer-based blitting directly into WriteableBitmap.
    /// </summary>
    public static class Yuv422Rasterizer
    {
        // 1024-byte clamp LUT centered at offset 384 (covers index range [-384, +639])
        private static readonly byte[] ClampTable = new byte[1024];

        static Yuv422Rasterizer()
        {
            for (int i = 0; i < 1024; i++)
            {
                int val = i - 384;
                ClampTable[i] = (byte)(val < 0 ? 0 : (val > 255 ? 255 : val));
            }
        }

        public static byte Clamp(int val)
        {
            int idx = val + 384;
            if ((uint)idx < 1024) return ClampTable[idx];
            return val < 0 ? (byte)0 : (byte)255;
        }

        public static byte[] ConvertYuv422ToRgb24(ReadOnlySpan<byte> yuv, int width, int height)
        {
            int pixelCount = width * height;
            byte[] rgb = new byte[pixelCount * 3];
            ConvertYuv422ToRgb24(yuv, rgb.AsSpan(), width, height);
            return rgb;
        }

        public static void ConvertYuv422ToRgb24(ReadOnlySpan<byte> yuv, Span<byte> rgb, int width, int height)
        {
            int yuvIdx = 0;
            int rgbIdx = 0;

            while (yuvIdx <= yuv.Length - 4 && rgbIdx <= rgb.Length - 6)
            {
                byte u = yuv[yuvIdx];
                byte y0 = yuv[yuvIdx + 1];
                byte v = yuv[yuvIdx + 2];
                byte y1 = yuv[yuvIdx + 3];

                int dU = u - 128;
                int dV = v - 128;

                int deltaR = (1440 * dU) >> 10;
                int deltaG = (-354 * dV - 734 * dU) >> 10;
                int deltaB = (1822 * dV) >> 10;

                rgb[rgbIdx] = Clamp(y0 + deltaR);
                rgb[rgbIdx + 1] = Clamp(y0 + deltaG);
                rgb[rgbIdx + 2] = Clamp(y0 + deltaB);

                rgb[rgbIdx + 3] = Clamp(y1 + deltaR);
                rgb[rgbIdx + 4] = Clamp(y1 + deltaG);
                rgb[rgbIdx + 5] = Clamp(y1 + deltaB);

                yuvIdx += 4;
                rgbIdx += 6;
            }
        }

        public static byte[] ConvertYuv422ToBgr24(ReadOnlySpan<byte> yuv, int width, int height)
        {
            int pixelCount = width * height;
            byte[] bgr = new byte[pixelCount * 3];
            ConvertYuv422ToBgr24(yuv, bgr.AsSpan(), width, height);
            return bgr;
        }

        public static void ConvertYuv422ToBgr24(ReadOnlySpan<byte> yuv, Span<byte> bgr, int width, int height)
        {
            int yuvIdx = 0;
            int bgrIdx = 0;

            while (yuvIdx <= yuv.Length - 4 && bgrIdx <= bgr.Length - 6)
            {
                byte u = yuv[yuvIdx];
                byte y0 = yuv[yuvIdx + 1];
                byte v = yuv[yuvIdx + 2];
                byte y1 = yuv[yuvIdx + 3];

                int dU = u - 128;
                int dV = v - 128;

                int deltaR = (1440 * dU) >> 10;
                int deltaG = (-354 * dV - 734 * dU) >> 10;
                int deltaB = (1822 * dV) >> 10;

                bgr[bgrIdx] = Clamp(y0 + deltaB);
                bgr[bgrIdx + 1] = Clamp(y0 + deltaG);
                bgr[bgrIdx + 2] = Clamp(y0 + deltaR);

                bgr[bgrIdx + 3] = Clamp(y1 + deltaB);
                bgr[bgrIdx + 4] = Clamp(y1 + deltaG);
                bgr[bgrIdx + 5] = Clamp(y1 + deltaR);

                yuvIdx += 4;
                bgrIdx += 6;
            }
        }

        public static unsafe void ConvertYuv422ToBgr24(byte* pYuv, byte* pBgr, int pixelCount)
        {
            fixed (byte* pClamp = &ClampTable[384])
            {
                int quadCount = pixelCount >> 1;
                for (int i = 0; i < quadCount; i++)
                {
                    int u = pYuv[0] - 128;
                    int y0 = pYuv[1];
                    int v = pYuv[2] - 128;
                    int y1 = pYuv[3];
                    pYuv += 4;

                    int deltaR = (1440 * u) >> 10;
                    int deltaG = (-354 * v - 734 * u) >> 10;
                    int deltaB = (1822 * v) >> 10;

                    pBgr[0] = pClamp[y0 + deltaB];
                    pBgr[1] = pClamp[y0 + deltaG];
                    pBgr[2] = pClamp[y0 + deltaR];

                    pBgr[3] = pClamp[y1 + deltaB];
                    pBgr[4] = pClamp[y1 + deltaG];
                    pBgr[5] = pClamp[y1 + deltaR];

                    pBgr += 6;
                }
            }
        }

        public static unsafe void ConvertYuv422ToBgr24Stride(byte* pYuv, byte* pBackBuffer, int width, int height, int stride)
        {
            int yuvLineBytes = width * 2;
            for (int line = 0; line < height; line++)
            {
                byte* pSrcLine = pYuv + (line * yuvLineBytes);
                byte* pDstLine = pBackBuffer + (line * stride);
                ConvertYuv422ToBgr24(pSrcLine, pDstLine, width);
            }
        }

        public static void ConvertYuv422SliceToBgra(byte[] src, int srcOffset, int srcLength, byte[] dest, int destOffset)
        {
            for (int i = 0; i <= srcLength - 4; i += 4)
            {
                byte y0 = src[srcOffset + i];
                byte u  = src[srcOffset + i + 1];
                byte y1 = src[srcOffset + i + 2];
                byte v  = src[srcOffset + i + 3];

                int c0 = y0 - 16;
                int d  = u - 128;
                int c1 = y1 - 16;
                int e  = v - 128;

                int r0 = Math.Clamp((298 * c0 + 409 * e + 128) >> 8, 0, 255);
                int g0 = Math.Clamp((298 * c0 - 100 * d - 208 * e + 128) >> 8, 0, 255);
                int b0 = Math.Clamp((298 * c0 + 516 * d + 128) >> 8, 0, 255);

                int p0 = destOffset + (i * 2);
                if (p0 + 7 < dest.Length)
                {
                    dest[p0]     = (byte)b0;
                    dest[p0 + 1] = (byte)g0;
                    dest[p0 + 2] = (byte)r0;
                    dest[p0 + 3] = 255;

                    int r1 = Math.Clamp((298 * c1 + 409 * e + 128) >> 8, 0, 255);
                    int g1 = Math.Clamp((298 * c1 - 100 * d - 208 * e + 128) >> 8, 0, 255);
                    int b1 = Math.Clamp((298 * c1 + 516 * d + 128) >> 8, 0, 255);

                    dest[p0 + 4] = (byte)b1;
                    dest[p0 + 5] = (byte)g1;
                    dest[p0 + 6] = (byte)r1;
                    dest[p0 + 7] = 255;
                }
            }
        }
    }
}
