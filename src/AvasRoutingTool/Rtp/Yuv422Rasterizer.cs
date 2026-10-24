using System;

namespace AvasRoutingTool.Rtp
{
    public static class Yuv422Rasterizer
    {
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

                // Pixel 0
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

                    // Pixel 1
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
