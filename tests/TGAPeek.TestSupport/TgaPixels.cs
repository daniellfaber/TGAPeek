namespace TGAPeek.TestSupport;

/// <summary>Stored-pixel helpers and expected decoded values, written independently of the decoder.</summary>
public static class TgaPixels
{
    public static byte[] Bgr(int r, int g, int b) => [(byte)b, (byte)g, (byte)r];

    public static byte[] Bgra(int r, int g, int b, int a) => [(byte)b, (byte)g, (byte)r, (byte)a];

    /// <summary>A stored 5-5-5 word; <paramref name="attribute"/> sets bit 15.</summary>
    public static byte[] Word(int r5, int g5, int b5, bool attribute = false)
    {
        int word = (attribute ? 0x8000 : 0) | r5 << 10 | g5 << 5 | b5;
        return [(byte)word, (byte)(word >> 8)];
    }

    public static byte[] Index(int index, int bits) => bits == 8 ? [(byte)index] : [(byte)index, (byte)(index >> 8)];

    /// <summary>0xAARRGGBB.</summary>
    public static uint Argb(int a, int r, int g, int b) => (uint)a << 24 | (uint)r << 16 | (uint)g << 8 | (uint)b;

    /// <summary>The documented 5-to-8-bit expansion: v·8 + v/4 (bit replication).</summary>
    public static int Five(int v) => v * 8 + v / 4;

    /// <summary>The decoded value the viewer must produce for one stored pixel of <paramref name="spec"/>.</summary>
    public static uint Expected(TgaSpec spec, byte[] stored)
    {
        switch (spec.ImageType)
        {
            case 2 or 10:
                return Direct(spec.PixelDepth, stored);
            case 3 or 11:
                return spec.PixelDepth == 8
                    ? Argb(255, stored[0], stored[0], stored[0])
                    : Argb(stored[1], stored[0], stored[0], stored[0]);
            default:
                int index = spec.PixelDepth == 8 ? stored[0] : stored[0] | stored[1] << 8;
                int i = index - spec.ColorMapFirstIndex;
                return i >= 0 && i < spec.ColorMap!.Length ? Direct(spec.ColorMapEntryBits, spec.ColorMap[i]) : 0;
        }
    }

    public static uint Direct(int bits, byte[] s)
    {
        switch (bits)
        {
            case 24:
                return Argb(255, s[2], s[1], s[0]);
            case 32:
                return Argb(s[3], s[2], s[1], s[0]);
            default:
                int word = s[0] | s[1] << 8;
                int alpha = bits == 16 && (word & 0x8000) == 0 ? 0 : 255;
                return Argb(alpha, Five((word >> 10) & 31), Five((word >> 5) & 31), Five(word & 31));
        }
    }

    /// <summary>Expected decoded image, top row first.</summary>
    public static uint[] ExpectedImage(TgaSpec spec) => spec.Pixels.Select(p => Expected(spec, p)).ToArray();

    /// <summary>Random stored pixels with frequent repeats so RLE run packets occur.</summary>
    public static byte[][] RandomPixels(Random random, int count, Func<Random, byte[]> make, double repeatChance = 0.35)
    {
        var pixels = new byte[count][];
        for (int i = 0; i < count; i++)
        {
            pixels[i] = i > 0 && random.NextDouble() < repeatChance ? pixels[i - 1] : make(random);
        }

        return pixels;
    }

    public static Func<Random, byte[]> RandomStored(int depth) => depth switch
    {
        8 => r => [(byte)r.Next(256)],
        24 => r => [(byte)r.Next(256), (byte)r.Next(256), (byte)r.Next(256)],
        32 => r => [(byte)r.Next(256), (byte)r.Next(256), (byte)r.Next(256), (byte)r.Next(256)],
        _ => r => [(byte)r.Next(256), (byte)r.Next(256)],
    };
}
