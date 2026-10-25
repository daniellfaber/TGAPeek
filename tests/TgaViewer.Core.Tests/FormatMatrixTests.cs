using TgaViewer.TestSupport;
using static TgaViewer.Core.Tests.Helpers;

namespace TgaViewer.Core.Tests;

/// <summary>
/// Every supported combination of image type, depth, palette entry size, origin, interleaving and
/// compression, using random pixels. Expected values come from TgaPixels, written separately from
/// the decoder; the builder itself is checked against hand-written bytes in GoldenTests.
/// </summary>
public class FormatMatrixTests
{
    private static readonly int[] Origins = [0, 1, 2, 3];
    private static readonly int[] Interleaves = [0, 1, 2];
    private static readonly bool[] Compressions = [false, true];

    public static IEnumerable<object[]> TrueColorCases() =>
        from depth in new[] { 15, 16, 24, 32 }
        from origin in Origins
        from interleave in Interleaves
        from rle in Compressions
        select new object[] { depth, origin, interleave, rle };

    [Theory]
    [MemberData(nameof(TrueColorCases))]
    public void TrueColor(int depth, int origin, int interleave, bool rle)
    {
        var random = new Random(depth * 1000 + origin * 100 + interleave * 10 + (rle ? 1 : 0));
        var spec = RandomSpec(rle ? (byte)10 : (byte)2, depth, origin, interleave, random, width: 7, height: 6);
        spec.AttributeBits = depth switch { 32 => 8, 16 => 1, _ => 0 };
        var image = Decode(spec);

        AssertPixels(TgaPixels.ExpectedImage(spec), image);
        Assert.False(image.IsPartial);
        Assert.DoesNotContain(image.Warnings, w => !w.IsNote);
    }

    public static IEnumerable<object[]> GreyCases() =>
        from depth in new[] { 8, 16 }
        from origin in Origins
        from interleave in Interleaves
        from rle in Compressions
        select new object[] { depth, origin, interleave, rle };

    [Theory]
    [MemberData(nameof(GreyCases))]
    public void Greyscale(int depth, int origin, int interleave, bool rle)
    {
        var random = new Random(depth * 1000 + origin * 100 + interleave * 10 + (rle ? 1 : 0));
        var spec = RandomSpec(rle ? (byte)11 : (byte)3, depth, origin, interleave, random, width: 5, height: 7);
        spec.AttributeBits = depth == 16 ? 8 : 0;
        AssertPixels(TgaPixels.ExpectedImage(spec), Decode(spec));
    }

    public static IEnumerable<object[]> ColorMappedCases() =>
        from indexBits in new[] { 8, 16 }
        from entryBits in new[] { 15, 16, 24, 32 }
        from origin in new[] { 0, 3 }
        from interleave in new[] { 0, 2 }
        from rle in Compressions
        select new object[] { indexBits, entryBits, origin, interleave, rle };

    [Theory]
    [MemberData(nameof(ColorMappedCases))]
    public void ColorMapped(int indexBits, int entryBits, int origin, int interleave, bool rle)
    {
        var random = new Random(indexBits * 10000 + entryBits * 1000 + origin * 100 + interleave * 10 + (rle ? 1 : 0));
        int first = indexBits == 8 ? 7 : 1000;
        const int length = 40;
        var spec = RandomSpec(
            rle ? (byte)9 : (byte)1,
            indexBits,
            origin,
            interleave,
            random,
            width: 6,
            height: 5,
            make: r => TgaPixels.Index(first + r.Next(length), indexBits));
        spec.ColorMapFirstIndex = (ushort)first;
        spec.ColorMapEntryBits = (byte)entryBits;
        spec.ColorMap = Enumerable.Range(0, length).Select(_ => TgaPixels.RandomStored(entryBits)(random)).ToArray();
        spec.AttributeBits = entryBits switch { 32 => 8, 16 => 1, _ => 0 };

        var image = Decode(spec);
        AssertPixels(TgaPixels.ExpectedImage(spec), image);
        Assert.Null(image.InvalidIndexPixels);
        for (int y = 0; y < spec.Height; y++)
        {
            for (int x = 0; x < spec.Width; x++)
            {
                byte[] stored = spec.Pixels[y * spec.Width + x];
                int expectedIndex = indexBits == 8 ? stored[0] : stored[0] | stored[1] << 8;
                Assert.True(image.TryGetPaletteIndex(x, y, out int index));
                Assert.Equal(expectedIndex, index);
            }
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 9)]
    [InlineData(9, 1)]
    [InlineData(129, 3)]
    [InlineData(300, 2)]
    public void UnusualSizes(int width, int height)
    {
        foreach (bool rle in Compressions)
        {
            var spec = RandomSpec(rle ? (byte)10 : (byte)2, 24, 0, 0, new Random(width * height), width, height);
            AssertPixels(TgaPixels.ExpectedImage(spec), Decode(spec));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    public void ImageIdLengths(int length)
    {
        var spec = RandomSpec(2, 24, 2, 0, new Random(length), 3, 2);
        spec.ImageId = Enumerable.Range(0, length).Select(i => (byte)(i + 1)).ToArray();
        var image = Decode(spec);
        AssertPixels(TgaPixels.ExpectedImage(spec), image);
        Assert.Equal(spec.ImageId, image.Metadata.ImageId);
    }

    [Fact]
    public void RleAndUncompressedDecodeIdentically()
    {
        foreach (var (type, rleType, depth) in new[] { (2, 10, 15), (2, 10, 16), (2, 10, 24), (2, 10, 32), (3, 11, 8), (3, 11, 16) })
        {
            var spec = RandomSpec((byte)type, depth, 1, 0, new Random(depth), 33, 17);
            var plain = Pixels(Decode(spec));
            spec.ImageType = (byte)rleType;
            Assert.Equal(plain, Pixels(Decode(spec)));
        }
    }

    [Fact]
    public void RlePacketsThatStopAtRowEnds_ProduceNoNote()
    {
        var spec = RandomSpec(10, 24, 2, 0, new Random(5), 9, 9);
        spec.RlePacketsMayCrossRows = false;
        var image = Decode(spec);
        AssertPixels(TgaPixels.ExpectedImage(spec), image);
        Assert.Empty(image.Warnings);
    }

    [Fact]
    public void AllSamples_DecodeOrFailAsDocumented()
    {
        foreach (var (name, bytes) in TgaSamples.All())
        {
            try
            {
                var image = TgaDecoder.Decode(bytes, new TgaDecodeOptions { AllowPartial = true });
                Assert.True(image.Width > 0, name);
            }
            catch (TgaException ex)
            {
                Assert.True(name.Contains("not a TGA") || name.Contains("unsupported"), $"{name}: {ex.Message}");
            }
        }
    }
}
