namespace TgaViewer.TestSupport;

/// <summary>
/// Small files with seeded random pixels, decoded by an independent decoder (Pillow) to produce the
/// reference outputs in tests/TgaViewer.Core.Tests/Reference. Random values exercise every bit of every
/// channel; odd sizes catch row-stride mistakes. The committed input files are what the tests read, so
/// after changing this catalogue, regenerate the inputs and the Pillow outputs (tools/reference/README.md).
/// </summary>
public static class TgaReferenceSamples
{
    private const int Width = 23;
    private const int Height = 17;

    public static IEnumerable<(string Name, byte[] Bytes)> All()
    {
        yield return ("tc24-bottom-left.tga", Bytes(TrueColor(1, 24, origin: 0)));
        yield return ("tc24-top-left.tga", Bytes(TrueColor(2, 24, origin: 2)));
        yield return ("tc24-bottom-right.tga", Bytes(TrueColor(3, 24, origin: 1)));
        yield return ("tc24-top-right.tga", Bytes(TrueColor(4, 24, origin: 3)));
        yield return ("tc32-alpha-bits-8.tga", Bytes(TrueColor(5, 32, attributeBits: 8)));
        yield return ("tc32-alpha-bits-0-top-left.tga", Bytes(TrueColor(6, 32, origin: 2)));
        yield return ("tc16-attribute-bits-1.tga", Bytes(TrueColor(7, 16, attributeBits: 1)));
        yield return ("tc16-attribute-bits-0-top-left.tga", Bytes(TrueColor(8, 16, origin: 2)));
        yield return ("tc15.tga", Bytes(TrueColor(9, 15)));
        yield return ("tc24-image-id.tga", Bytes(WithImageId(TrueColor(10, 24, origin: 2))));
        yield return ("tc24-unused-colour-map.tga", Bytes(WithUnusedColorMap(TrueColor(11, 24))));
        yield return ("grey8-bottom-left.tga", Bytes(Grey(12, 8, origin: 0)));
        yield return ("grey8-top-right.tga", Bytes(Grey(13, 8, origin: 3)));
        yield return ("grey16-with-alpha.tga", Bytes(Grey(14, 16, origin: 2, attributeBits: 8)));
        yield return ("cm8-palette24.tga", Bytes(ColorMapped(15, 8, 24, first: 0, length: 200)));
        yield return ("cm8-palette24-from-index-16.tga", Bytes(ColorMapped(16, 8, 24, first: 16, length: 200, origin: 0)));
        yield return ("cm8-palette32.tga", Bytes(ColorMapped(17, 8, 32, first: 0, length: 64, attributeBits: 8)));
        yield return ("cm8-palette16.tga", Bytes(ColorMapped(18, 8, 16, first: 0, length: 64, attributeBits: 1)));
        yield return ("cm8-palette15.tga", Bytes(ColorMapped(19, 8, 15, first: 0, length: 64)));
        yield return ("cm16-palette24-from-index-300.tga", Bytes(ColorMapped(20, 16, 24, first: 300, length: 400)));
        yield return ("rle-tc24-within-rows.tga", Bytes(TrueColor(21, 24, rle: true, crossRows: false)));
        yield return ("rle-tc24-across-rows-top-left.tga", Bytes(TrueColor(22, 24, origin: 2, rle: true)));
        yield return ("rle-tc24-across-rows-top-right.tga", Bytes(TrueColor(23, 24, origin: 3, rle: true)));
        yield return ("rle-tc24-wide-rows.tga", Bytes(TrueColor(24, 24, rle: true, crossRows: false, width: 300, height: 5)));
        yield return ("rle-tc32-across-rows.tga", Bytes(TrueColor(25, 32, attributeBits: 8, rle: true)));
        yield return ("rle-tc16-across-rows.tga", Bytes(TrueColor(26, 16, attributeBits: 1, rle: true)));
        yield return ("rle-grey8-across-rows.tga", Bytes(Grey(27, 8, origin: 0, rle: true)));
        yield return ("rle-grey16-with-alpha-within-rows.tga", Bytes(Grey(28, 16, origin: 2, attributeBits: 8, rle: true, crossRows: false)));
        yield return ("rle-cm8-palette24-within-rows.tga", Bytes(ColorMapped(29, 8, 24, first: 0, length: 32, rle: true, crossRows: false)));
        yield return ("interleaved-two-way.tga", Bytes(TrueColor(30, 24, origin: 2, interleave: 1)));
        yield return ("interleaved-four-way.tga", Bytes(TrueColor(31, 24, interleave: 2)));
        yield return ("tga2-attributes-3-all-structures.tga", Bytes(WithAllStructures(TrueColor(32, 32, attributeBits: 8), 32)));
        yield return ("tga2-attributes-0.tga", Bytes(WithAttributesType(TrueColor(33, 32, attributeBits: 8), 0)));
        yield return ("tga2-attributes-4-premultiplied.tga", Bytes(WithAttributesType(TrueColor(34, 32, attributeBits: 8, origin: 2), 4)));
        yield return ("tga2-rle-attributes-3.tga", Bytes(WithAttributesType(TrueColor(35, 32, attributeBits: 8, rle: true), 3)));
    }

    private static byte[] Bytes(TgaSpec spec) => TgaBuilder.Bytes(spec);

    private static TgaSpec TrueColor(int seed, int depth, int origin = 0, int attributeBits = 0, bool rle = false, bool crossRows = true, int interleave = 0, int width = Width, int height = Height) =>
        Spec(seed, rle ? (byte)10 : (byte)2, depth, origin, attributeBits, rle, crossRows, interleave, width, height);

    private static TgaSpec Grey(int seed, int depth, int origin, int attributeBits = 0, bool rle = false, bool crossRows = true) =>
        Spec(seed, rle ? (byte)11 : (byte)3, depth, origin, attributeBits, rle, crossRows, interleave: 0, Width, Height);

    private static TgaSpec Spec(int seed, byte type, int depth, int origin, int attributeBits, bool rle, bool crossRows, int interleave, int width, int height) => new()
    {
        ImageType = type,
        Width = width,
        Height = height,
        PixelDepth = (byte)depth,
        AttributeBits = attributeBits,
        RightToLeft = (origin & 1) != 0,
        TopToBottom = (origin & 2) != 0,
        Interleave = interleave,
        RlePacketsMayCrossRows = crossRows,
        Pixels = TgaPixels.RandomPixels(new Random(seed), width * height, TgaPixels.RandomStored(depth == 15 ? 16 : depth), rle ? 0.5 : 0.1),
    };

    private static TgaSpec ColorMapped(int seed, int indexBits, int entryBits, int first, int length, int origin = 2, int attributeBits = 0, bool rle = false, bool crossRows = true)
    {
        var random = new Random(seed);
        var entry = TgaPixels.RandomStored(entryBits == 15 ? 16 : entryBits);
        return new TgaSpec
        {
            ImageType = rle ? (byte)9 : (byte)1,
            Width = Width,
            Height = Height,
            PixelDepth = (byte)indexBits,
            AttributeBits = attributeBits,
            RightToLeft = (origin & 1) != 0,
            TopToBottom = (origin & 2) != 0,
            ColorMapFirstIndex = (ushort)first,
            ColorMapEntryBits = (byte)entryBits,
            RlePacketsMayCrossRows = crossRows,
            ColorMap = Enumerable.Range(0, length).Select(_ => entry(random)).ToArray(),
            Pixels = TgaPixels.RandomPixels(random, Width * Height, r => TgaPixels.Index(first + r.Next(length), indexBits), rle ? 0.5 : 0.1),
        };
    }

    private static TgaSpec WithImageId(TgaSpec spec)
    {
        spec.ImageId = "Reference sample with an image ID field"u8.ToArray();
        return spec;
    }

    /// <summary>A true-colour image that still carries a colour map, which decoders must skip.</summary>
    private static TgaSpec WithUnusedColorMap(TgaSpec spec)
    {
        var random = new Random(1000);
        spec.ColorMapEntryBits = 24;
        spec.ColorMap = Enumerable.Range(0, 5).Select(_ => TgaPixels.RandomStored(24)(random)).ToArray();
        return spec;
    }

    private static TgaSpec WithAttributesType(TgaSpec spec, byte type)
    {
        spec.Tga2 = new Tga2Spec { AttributesType = type, SoftwareId = "TgaViewer reference samples" };
        return spec;
    }

    private static TgaSpec WithAllStructures(TgaSpec spec, int depth)
    {
        var random = new Random(2000);
        spec.Tga2 = new Tga2Spec
        {
            AuthorName = "TGA Viewer reference samples",
            AuthorComments = ["Every optional TGA 2.0 structure."],
            SoftwareId = "TgaViewer reference samples",
            GammaNumerator = 22,
            GammaDenominator = 10,
            AttributesType = 3,
            ColorCorrectionTable = Enumerable.Range(0, 1024).Select(_ => (ushort)random.Next(65536)).ToArray(),
            StampWidth = 4,
            StampHeight = 3,
            StampPixels = Enumerable.Range(0, 12).Select(_ => TgaPixels.RandomStored(depth)(random)).ToArray(),
            IncludeScanLineTable = true,
            DeveloperFields = [(1000, "Developer data"u8.ToArray())],
        };
        return spec;
    }
}
