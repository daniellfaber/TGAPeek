namespace TgaViewer.TestSupport;

/// <summary>
/// Named sample files covering the supported variants (plus some invalid and unsupported ones) for
/// manual testing. Every image shows the same test pattern: red, green, blue and white quadrants
/// (top-left, top-right, bottom-left, bottom-right) and a black 3 × 3 marker in the top-left corner,
/// so a wrong orientation is obvious. Where alpha is stored it falls from 255 (left) to 0 (right).
/// </summary>
public static class TgaSamples
{
    public const int Width = 64;
    public const int Height = 48;

    public static (int R, int G, int B, int A) Pattern(int x, int y, int width = Width, int height = Height)
    {
        int a = width == 1 ? 255 : 255 - x * 255 / (width - 1);
        if (x < 3 && y < 3)
        {
            return (0, 0, 0, a);
        }

        bool right = x >= width / 2;
        bool bottom = y >= height / 2;
        return (right, bottom) switch
        {
            (false, false) => (255, 0, 0, a),
            (true, false) => (0, 255, 0, a),
            (false, true) => (0, 0, 255, a),
            _ => (255, 255, 255, a),
        };
    }

    public static IEnumerable<(string Name, byte[] Bytes)> All()
    {
        yield return ("01 true-colour 24-bit (bottom-left).tga", Bytes(TrueColor(24, origin: 0)));
        yield return ("02 true-colour 24-bit (top-left).tga", Bytes(TrueColor(24, origin: 2)));
        yield return ("03 true-colour 24-bit (bottom-right).tga", Bytes(TrueColor(24, origin: 1)));
        yield return ("04 true-colour 24-bit (top-right).tga", Bytes(TrueColor(24, origin: 3)));
        yield return ("05 true-colour 32-bit alpha.tga", Bytes(TrueColor(32, attributeBits: 8)));
        yield return ("06 true-colour 32-bit, header declares no alpha.tga", Bytes(TrueColor(32, attributeBits: 0)));
        yield return ("07 true-colour 32-bit, alpha all zero.tga", Bytes(TrueColor(32, attributeBits: 8, alphaOverride: 0)));
        yield return ("08 true-colour 16-bit attribute bit.tga", Bytes(TrueColor(16, attributeBits: 1)));
        yield return ("09 true-colour 15-bit.tga", Bytes(TrueColor(15)));
        yield return ("10 greyscale 8-bit.tga", Bytes(Grey(8)));
        yield return ("11 greyscale 16-bit with alpha.tga", Bytes(Grey(16, attributeBits: 8)));
        yield return ("12 colour-mapped 8-bit index, 24-bit palette from index 16.tga", Bytes(ColorMapped(8, 24, firstIndex: 16)));
        yield return ("13 colour-mapped 16-bit index, 32-bit palette from index 300.tga", Bytes(ColorMapped(16, 32, firstIndex: 300, attributeBits: 8)));
        yield return ("14 colour-mapped 8-bit index, 16-bit palette.tga", Bytes(ColorMapped(8, 16, firstIndex: 0, attributeBits: 1)));
        yield return ("15 RLE true-colour 32-bit alpha.tga", Bytes(TrueColor(32, attributeBits: 8, rle: true)));
        yield return ("16 RLE colour-mapped 8-bit.tga", Bytes(ColorMapped(8, 24, firstIndex: 0, rle: true)));
        yield return ("17 RLE greyscale 8-bit.tga", Bytes(Grey(8, rle: true)));
        yield return ("18 RLE packets cross rows.tga", Bytes(TrueColor(24, rle: true, crossRows: true)));
        yield return ("19 interleaved two-way.tga", Bytes(TrueColor(24, origin: 2, interleave: 1)));
        yield return ("20 interleaved four-way.tga", Bytes(TrueColor(24, origin: 0, interleave: 2)));
        yield return ("21 TGA 2.0 premultiplied alpha with metadata.tga", Bytes(Tga2Premultiplied()));
        yield return ("22 TGA 2.0 alpha type 2 (retain, not applied).tga", Bytes(WithAttributesType(TrueColor(32, attributeBits: 8), 2)));
        yield return ("23 truncated RLE (partial image).tga", Truncated(Bytes(TrueColor(24, origin: 2, rle: true)), 0.6));
        yield return ("24 invalid palette indices.tga", Bytes(InvalidIndices()));
        yield return ("25 not a TGA file.tga", "This is a text file, not an image."u8.ToArray());
        yield return ("26 unsupported Huffman-compressed type 32.tga", Bytes(UnsupportedType32()));
        yield return ("27 unicode name - ünïcødé ✓.tga", Bytes(TrueColor(24, origin: 2)));
    }

    public static TgaSpec TrueColor(int depth, int origin = 0, int attributeBits = 0, int? alphaOverride = null, bool rle = false, bool crossRows = true, int interleave = 0, int width = Width, int height = Height)
    {
        var spec = NewSpec(rle ? (byte)10 : (byte)2, depth, origin, attributeBits, interleave, width, height);
        spec.RlePacketsMayCrossRows = crossRows;
        spec.Pixels = Pixels(width, height, (r, g, b, a) =>
        {
            a = alphaOverride ?? a;
            return depth switch
            {
                24 => TgaPixels.Bgr(r, g, b),
                32 => TgaPixels.Bgra(r, g, b, a),
                16 => TgaPixels.Word(r >> 3, g >> 3, b >> 3, a >= 128),
                _ => TgaPixels.Word(r >> 3, g >> 3, b >> 3),
            };
        });
        return spec;
    }

    public static TgaSpec Grey(int depth, int attributeBits = 0, bool rle = false)
    {
        var spec = NewSpec(rle ? (byte)11 : (byte)3, depth, 2, attributeBits, 0, Width, Height);
        spec.Pixels = Pixels(Width, Height, (r, g, b, a) =>
        {
            byte v = (byte)((r * 299 + g * 587 + b * 114) / 1000);
            return depth == 8 ? [v] : [v, (byte)a];
        });
        return spec;
    }

    public static TgaSpec ColorMapped(int indexBits, int entryBits, int firstIndex, int attributeBits = 0, bool rle = false)
    {
        var spec = NewSpec(rle ? (byte)9 : (byte)1, indexBits, 2, attributeBits, 0, Width, Height);
        var palette = new List<(int R, int G, int B, int A)>();
        var entries = new List<byte[]>();
        spec.Pixels = Pixels(Width, Height, (r, g, b, a) =>
        {
            int alpha = entryBits == 32 ? a / 64 * 85 : entryBits == 16 ? (a >= 128 ? 255 : 0) : 255;
            var colour = (r, g, b, alpha);
            int i = palette.IndexOf(colour);
            if (i < 0)
            {
                i = palette.Count;
                palette.Add(colour);
                entries.Add(entryBits switch
                {
                    24 => TgaPixels.Bgr(r, g, b),
                    32 => TgaPixels.Bgra(r, g, b, alpha),
                    16 => TgaPixels.Word(r >> 3, g >> 3, b >> 3, alpha == 255),
                    _ => TgaPixels.Word(r >> 3, g >> 3, b >> 3),
                });
            }

            return TgaPixels.Index(firstIndex + i, indexBits);
        });
        spec.ColorMap = entries.ToArray();
        spec.ColorMapFirstIndex = (ushort)firstIndex;
        spec.ColorMapEntryBits = (byte)entryBits;
        return spec;
    }

    public static byte[] Bytes(TgaSpec spec) => TgaBuilder.Bytes(spec);

    private static TgaSpec NewSpec(byte type, int depth, int origin, int attributeBits, int interleave, int width, int height) => new()
    {
        ImageType = type,
        Width = width,
        Height = height,
        PixelDepth = (byte)depth,
        AttributeBits = attributeBits,
        RightToLeft = (origin & 1) != 0,
        TopToBottom = (origin & 2) != 0,
        Interleave = interleave,
    };

    private static byte[][] Pixels(int width, int height, Func<int, int, int, int, byte[]> store)
    {
        var pixels = new byte[width * height][];
        var cache = new Dictionary<(int, int, int, int), byte[]>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var (r, g, b, a) = Pattern(x, y, width, height);
                if (!cache.TryGetValue((r, g, b, a), out byte[]? stored))
                {
                    stored = store(r, g, b, a);
                    cache[(r, g, b, a)] = stored;
                }

                pixels[y * width + x] = stored;
            }
        }

        return pixels;
    }

    private static TgaSpec Tga2Premultiplied()
    {
        var spec = NewSpec(2, 32, 2, 8, 0, Width, Height);
        spec.Pixels = Pixels(Width, Height, (r, g, b, a) => TgaPixels.Bgra(r * a / 255, g * a / 255, b * a / 255, a));
        var stamp = new byte[8 * 6][];
        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                var (r, g, b, a) = Pattern(x * 8, y * 8);
                stamp[y * 8 + x] = TgaPixels.Bgra(r * a / 255, g * a / 255, b * a / 255, a);
            }
        }

        spec.Tga2 = new Tga2Spec
        {
            AuthorName = "TGA Viewer sample generator",
            AuthorComments = ["Premultiplied alpha (attributes type 4).", "Includes every optional TGA 2.0 structure."],
            Timestamp = [10, 25, 2026, 12, 0, 0],
            JobName = "Sample 21",
            SoftwareId = "TgaViewer.SampleGenerator",
            SoftwareVersionNumber = 100,
            KeyColor = 0xFF000000,
            AspectNumerator = 1,
            AspectDenominator = 1,
            GammaNumerator = 22,
            GammaDenominator = 10,
            AttributesType = 4,
            ColorCorrectionTable = Enumerable.Range(0, 1024).Select(i => (ushort)(i / 4 * 257)).ToArray(),
            StampWidth = 8,
            StampHeight = 6,
            StampPixels = stamp,
            IncludeScanLineTable = true,
            DeveloperFields = [(1000, "Developer data 1"u8.ToArray()), (1001, [1, 2, 3, 4])],
        };
        return spec;
    }

    private static TgaSpec WithAttributesType(TgaSpec spec, byte type)
    {
        spec.Tga2 = new Tga2Spec { AttributesType = type, SoftwareId = "TgaViewer.SampleGenerator" };
        return spec;
    }

    private static TgaSpec InvalidIndices()
    {
        var spec = ColorMapped(8, 24, firstIndex: 0);
        for (int i = 0; i < spec.Pixels.Length; i++)
        {
            int x = i % Width, y = i / Width;
            if (x >= 20 && x < 44 && y >= 16 && y < 32)
            {
                spec.Pixels[i] = [200];
            }
        }

        return spec;
    }

    private static TgaSpec UnsupportedType32()
    {
        var spec = ColorMapped(8, 24, firstIndex: 0);
        spec.ImageType = 32;
        return spec;
    }

    private static byte[] Truncated(byte[] bytes, double fraction) => bytes[..(int)(bytes.Length * fraction)];
}
