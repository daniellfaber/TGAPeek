using TGAPeek.TestSupport;

namespace TGAPeek.Core.Tests;

internal static class Helpers
{
    public static TgaImage Decode(byte[] bytes, TgaDecodeOptions? options = null) => TgaDecoder.Decode(bytes, options);

    public static TgaImage Decode(TgaSpec spec, TgaDecodeOptions? options = null) => Decode(TgaBuilder.Bytes(spec), options);

    public static uint[] Pixels(TgaImage image)
    {
        var all = new uint[image.PixelCount];
        for (int y = 0; y < image.Height; y++)
        {
            image.GetRow(y).CopyTo(all.AsSpan(y * image.Width));
        }

        return all;
    }

    public static void AssertPixels(uint[] expected, TgaImage image)
    {
        uint[] actual = Pixels(image);
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i])
            {
                Assert.Fail($"Pixel ({i % image.Width}, {i / image.Width}): expected 0x{expected[i]:X8}, decoded 0x{actual[i]:X8}");
            }
        }
    }

    /// <summary>An 18-byte header, written field by field from the TGA specification.</summary>
    public static byte[] Header(
        byte idLength = 0,
        byte colorMapType = 0,
        byte imageType = 2,
        ushort firstIndex = 0,
        ushort mapLength = 0,
        byte entryBits = 0,
        ushort width = 1,
        ushort height = 1,
        byte depth = 24,
        byte descriptor = 0) =>
    [
        idLength, colorMapType, imageType,
        (byte)firstIndex, (byte)(firstIndex >> 8),
        (byte)mapLength, (byte)(mapLength >> 8),
        entryBits,
        0, 0, 0, 0,
        (byte)width, (byte)(width >> 8),
        (byte)height, (byte)(height >> 8),
        depth, descriptor,
    ];

    public static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    public static bool HasWarning(TgaImage image, TgaWarningCode code) => image.Warnings.Any(w => w.Code == code);

    /// <summary>A random image of the given type with stored pixels from <paramref name="make"/>.</summary>
    public static TgaSpec RandomSpec(byte type, int depth, int origin, int interleave, Random random, int width, int height, Func<Random, byte[]>? make = null) => new()
    {
        ImageType = type,
        PixelDepth = (byte)depth,
        Width = width,
        Height = height,
        RightToLeft = (origin & 1) != 0,
        TopToBottom = (origin & 2) != 0,
        Interleave = interleave,
        Pixels = TgaPixels.RandomPixels(random, width * height, make ?? TgaPixels.RandomStored(depth)),
    };
}
