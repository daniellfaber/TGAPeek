namespace TgaViewer.TestSupport;

/// <summary>Writes large, simple 32-bit images directly (the general builder is too slow for these).</summary>
public static class LargeTga
{
    /// <summary>Uncompressed, top-left origin, 8 alpha bits. Pixel (x, y) = B x, G y, R x ^ y, A 255 (low bytes).</summary>
    public static byte[] Uncompressed32(int width, int height)
    {
        var data = new byte[18 + (long)width * height * 4];
        Header(data, 2, width, height);
        long o = 18;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                data[o++] = (byte)x;
                data[o++] = (byte)y;
                data[o++] = (byte)(x ^ y);
                data[o++] = 255;
            }
        }

        return data;
    }

    /// <summary>RLE, top-left origin: each row alternates 128-pixel run packets and 128-pixel raw packets.</summary>
    public static byte[] Rle32(int width, int height)
    {
        var o = new MemoryStream();
        var header = new byte[18];
        Header(header, 10, width, height);
        o.Write(header);
        var raw = new byte[128 * 4];
        for (int y = 0; y < height; y++)
        {
            int x = 0;
            bool run = (y & 1) == 0;
            while (x < width)
            {
                int count = Math.Min(128, width - x);
                if (run)
                {
                    o.WriteByte((byte)(0x80 | (count - 1)));
                    o.Write([(byte)x, (byte)y, (byte)(x >> 8), 255]);
                }
                else
                {
                    o.WriteByte((byte)(count - 1));
                    for (int i = 0; i < count; i++)
                    {
                        raw[i * 4] = (byte)(x + i);
                        raw[i * 4 + 1] = (byte)y;
                        raw[i * 4 + 2] = (byte)((x + i) ^ y);
                        raw[i * 4 + 3] = (byte)(255 - i);
                    }

                    o.Write(raw, 0, count * 4);
                }

                x += count;
                run = !run;
            }
        }

        return o.ToArray();
    }

    private static void Header(byte[] data, byte type, int width, int height)
    {
        data[2] = type;
        data[12] = (byte)width;
        data[13] = (byte)(width >> 8);
        data[14] = (byte)height;
        data[15] = (byte)(height >> 8);
        data[16] = 32;
        data[17] = 0x28;
    }
}
