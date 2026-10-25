using TgaViewer.TestSupport;
using static TgaViewer.Core.Tests.Helpers;

namespace TgaViewer.Core.Tests;

public class MalformedInputTests
{
    private static readonly TgaDecodeOptions Partial = new() { AllowPartial = true };

    public static IEnumerable<object[]> TruncationFixtures()
    {
        var tga2 = TgaSamples.TrueColor(32, origin: 2, attributeBits: 8, width: 6, height: 4);
        tga2.Tga2 = new Tga2Spec { AuthorName = "x", AttributesType = 3, StampWidth = 1, StampHeight = 1, StampPixels = [TgaPixels.Bgra(1, 2, 3, 4)], IncludeScanLineTable = true, DeveloperFields = [(5, [9, 9])] };
        yield return ["TGA 2.0 32-bit", TgaBuilder.Bytes(tga2)];

        var colorMapped = TgaSamples.ColorMapped(16, 32, firstIndex: 300, attributeBits: 8, rle: true);
        colorMapped.ImageId = "id"u8.ToArray();
        yield return ["RLE colour-mapped, 16-bit indices", TgaBuilder.Bytes(colorMapped)];

        var grey = RandomSpec(11, 8, 0, 2, new Random(3), 5, 9);
        yield return ["RLE greyscale, four-way interleaved", TgaBuilder.Bytes(grey)];

        var word = RandomSpec(10, 16, 1, 1, new Random(4), 7, 3);
        yield return ["RLE 16-bit, two-way interleaved", TgaBuilder.Bytes(word)];
    }

    [Theory]
    [MemberData(nameof(TruncationFixtures))]
    public void TruncationAtEveryByte_IsHandled(string name, byte[] file)
    {
        var complete = Decode(file);
        for (int length = 0; length < file.Length; length++)
        {
            byte[] prefix = file[..length];

            try
            {
                var strict = Decode(prefix);
                Assert.False(strict.IsPartial, name);
            }
            catch (TgaInvalidFileException ex)
            {
                Assert.Contains(ex.Reason, new[] { TgaErrorReason.TruncatedHeader, TgaErrorReason.TruncatedImageId, TgaErrorReason.TruncatedColorMap, TgaErrorReason.TruncatedPixelData });
            }

            try
            {
                var image = Decode(prefix, Partial);
                if (image.IsPartial)
                {
                    Assert.Equal(image.PixelCount - image.DecodedPixelCount, image.MissingPixels!.CountSet());
                    AssertDecodedPixelsMatch(complete, image);
                }
                else
                {
                    Assert.Equal(image.PixelCount, image.DecodedPixelCount);
                }
            }
            catch (TgaInvalidFileException ex)
            {
                Assert.Contains(ex.Reason, new[] { TgaErrorReason.TruncatedHeader, TgaErrorReason.TruncatedImageId, TgaErrorReason.TruncatedColorMap });
            }
        }
    }

    /// <summary>Every pixel that was decoded from a truncated file has the same value as in the complete file.</summary>
    private static void AssertDecodedPixelsMatch(TgaImage complete, TgaImage partial)
    {
        for (int y = 0; y < partial.Height; y++)
        {
            for (int x = 0; x < partial.Width; x++)
            {
                if (!partial.IsMissing(x, y))
                {
                    Assert.Equal(complete.GetPixel(x, y), partial.GetPixel(x, y));
                }
            }
        }
    }

    [Theory]
    [InlineData((byte)0x20, new[] { "0,0", "1,0", "2,0", "0,1" })]
    [InlineData((byte)0x00, new[] { "0,2", "1,2", "2,2", "0,1" })]
    [InlineData((byte)0x30, new[] { "2,0", "1,0", "0,0", "2,1" })]
    public void PartialImage_KeepsDecodedPixelsInFileOrder(byte descriptor, string[] present)
    {
        // 3 × 3, 24-bit, cut after 4 whole pixels and one extra byte.
        byte[] pixels = Enumerable.Range(0, 9 * 3).Select(i => (byte)(i + 1)).ToArray();
        byte[] file = Concat(Header(width: 3, height: 3, descriptor: descriptor), pixels)[..(18 + 4 * 3 + 1)];

        var image = Decode(file, Partial);
        Assert.True(image.IsPartial);
        Assert.Equal(4, image.DecodedPixelCount);
        Assert.True(HasWarning(image, TgaWarningCode.TruncatedPixelData));
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                Assert.Equal(!present.Contains($"{x},{y}"), image.IsMissing(x, y));
            }
        }

        var strict = Assert.Throws<TgaInvalidFileException>(() => Decode(file));
        Assert.Equal(TgaErrorReason.TruncatedPixelData, strict.Reason);
    }

    [Fact]
    public void Rle_TruncatedInsideARunValue_KeepsEarlierPackets()
    {
        byte[] file = Concat(Header(imageType: 10, width: 4, descriptor: 0x20), [0x81, 1, 2, 3, 0x81, 4]);
        var image = Decode(file, Partial);
        Assert.Equal(2, image.DecodedPixelCount);
        Assert.True(image.IsMissing(2, 0));
        Assert.Equal(0xFF030201u, image.GetPixel(1, 0));
    }

    [Fact]
    public void Rle_TruncatedInsideARawPacket_KeepsWholePixels()
    {
        byte[] file = Concat(Header(imageType: 10, width: 4, descriptor: 0x20), [0x03, 1, 2, 3, 4, 5, 6, 7]);
        var image = Decode(file, Partial);
        Assert.Equal(2, image.DecodedPixelCount);
        Assert.Equal(0xFF060504u, image.GetPixel(1, 0));
    }

    public static IEnumerable<object[]> ClassificationCases()
    {
        byte[] somePixels = new byte[64];
        object[] Case(string name, byte[] header, Type type, TgaErrorReason reason) => [name, Concat(header, somePixels), type, reason];

        yield return ["empty file", Array.Empty<byte>(), typeof(TgaInvalidFileException), TgaErrorReason.TruncatedHeader];
        yield return Case("image type 0", Header(imageType: 0), typeof(TgaUnsupportedFormatException), TgaErrorReason.NoImageData);
        yield return Case("image type 32", Header(imageType: 32, depth: 8), typeof(TgaUnsupportedFormatException), TgaErrorReason.UnsupportedCompression);
        yield return Case("image type 33", Header(imageType: 33, depth: 8), typeof(TgaUnsupportedFormatException), TgaErrorReason.UnsupportedCompression);
        yield return Case("image type 5", Header(imageType: 5), typeof(TgaInvalidFileException), TgaErrorReason.UnknownImageType);
        yield return Case("image type 255", Header(imageType: 255), typeof(TgaInvalidFileException), TgaErrorReason.UnknownImageType);
        yield return Case("zero width", Header(width: 0, height: 4), typeof(TgaInvalidFileException), TgaErrorReason.ZeroDimensions);
        yield return Case("zero height", Header(width: 4, height: 0), typeof(TgaInvalidFileException), TgaErrorReason.ZeroDimensions);
        yield return Case("8-bit true-colour", Header(depth: 8), typeof(TgaUnsupportedFormatException), TgaErrorReason.UnsupportedPixelDepth);
        yield return Case("31-bit true-colour", Header(depth: 31), typeof(TgaUnsupportedFormatException), TgaErrorReason.UnsupportedPixelDepth);
        yield return Case("24-bit greyscale", Header(imageType: 3, depth: 24), typeof(TgaUnsupportedFormatException), TgaErrorReason.UnsupportedPixelDepth);
        yield return Case("24-bit indices", Header(colorMapType: 1, imageType: 1, mapLength: 2, entryBits: 24, depth: 24), typeof(TgaUnsupportedFormatException), TgaErrorReason.UnsupportedPixelDepth);
        yield return Case("colour-mapped without a map", Header(imageType: 1, depth: 8), typeof(TgaInvalidFileException), TgaErrorReason.MissingColorMap);
        yield return Case("empty colour map", Header(colorMapType: 1, imageType: 1, entryBits: 24, depth: 8), typeof(TgaInvalidFileException), TgaErrorReason.EmptyColorMap);
        yield return Case("8-bit palette entries", Header(colorMapType: 1, imageType: 1, mapLength: 2, entryBits: 8, depth: 8), typeof(TgaUnsupportedFormatException), TgaErrorReason.UnsupportedColorMapEntrySize);
        yield return Case("reserved interleave", Header(descriptor: 0xC0), typeof(TgaUnsupportedFormatException), TgaErrorReason.ReservedInterleave);
        yield return Case("reserved colour-map type with fields", Header(colorMapType: 7, mapLength: 2, entryBits: 24), typeof(TgaUnsupportedFormatException), TgaErrorReason.ReservedColorMapType);
        yield return Case("image ID cut short", Header(idLength: 200), typeof(TgaInvalidFileException), TgaErrorReason.TruncatedImageId);
        yield return Case("colour map cut short", Header(colorMapType: 1, imageType: 1, mapLength: 100, entryBits: 24, depth: 8), typeof(TgaInvalidFileException), TgaErrorReason.TruncatedColorMap);
        yield return Case("unused colour map cut short", Header(colorMapType: 1, imageType: 2, mapLength: 100, entryBits: 24), typeof(TgaInvalidFileException), TgaErrorReason.TruncatedColorMap);
    }

    [Theory]
    [MemberData(nameof(ClassificationCases))]
    public void ProblemsAreClassified(string name, byte[] file, Type exceptionType, TgaErrorReason reason)
    {
        var ex = Assert.ThrowsAny<TgaException>(() => Decode(file, Partial));
        Assert.True(ex.GetType() == exceptionType, $"{name}: expected {exceptionType.Name}, got {ex.GetType().Name}");
        Assert.Equal(reason, ex.Reason);
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void ShortHeader_IsTruncated()
    {
        for (int length = 1; length < 18; length++)
        {
            var ex = Assert.Throws<TgaInvalidFileException>(() => Decode(new byte[length]));
            Assert.Equal(TgaErrorReason.TruncatedHeader, ex.Reason);
        }
    }

    [Fact]
    public void HugeDimensions_AreRejectedBeforeAnythingLargeIsAllocated()
    {
        byte[] file = Header(width: 65535, height: 65535, depth: 32, descriptor: 0x28);
        Decode(Concat(Header(), [1, 2, 3])); // warm up
        long before = GC.GetAllocatedBytesForCurrentThread();
        var ex = Assert.Throws<TgaLimitExceededException>(() => Decode(file));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(TgaErrorReason.MemoryBudgetExceeded, ex.Reason);
        Assert.True(ex.Required > ex.Limit);
        Assert.True(allocated < 1_000_000, $"Allocated {allocated:N0} bytes before rejecting");
    }

    [Fact]
    public void ConfiguredLimits_AreEnforced()
    {
        byte[] file = Concat(Header(width: 11, height: 3), new byte[99]);
        Assert.Equal(TgaErrorReason.DimensionsTooLarge, Assert.Throws<TgaLimitExceededException>(() => Decode(file, new TgaDecodeOptions { MaxWidth = 10 })).Reason);
        Assert.Equal(TgaErrorReason.DimensionsTooLarge, Assert.Throws<TgaLimitExceededException>(() => Decode(file, new TgaDecodeOptions { MaxHeight = 2 })).Reason);
        Assert.Equal(TgaErrorReason.PixelCountTooLarge, Assert.Throws<TgaLimitExceededException>(() => Decode(file, new TgaDecodeOptions { MaxPixelCount = 32 })).Reason);
        Assert.Equal(TgaErrorReason.MemoryBudgetExceeded, Assert.Throws<TgaLimitExceededException>(() => Decode(file, new TgaDecodeOptions { MaxDecodeMemoryBytes = 1000 })).Reason);
        Assert.Equal(11, Decode(file).Width);
    }

    [Theory]
    [InlineData((byte)24, (byte)8)]
    [InlineData((byte)32, (byte)3)]
    [InlineData((byte)16, (byte)4)]
    [InlineData((byte)15, (byte)1)]
    public void MismatchedAttributeBits_AreReported_AndPixelsStillDecoded(byte depth, byte bits)
    {
        var image = Decode(Concat(Header(depth: depth, descriptor: (byte)(0x20 | bits)), new byte[4]));
        Assert.True(HasWarning(image, TgaWarningCode.AttributeBitsMismatch));
    }

    [Fact]
    public void ColorMapFields_WithoutAColorMap_AreIgnored()
    {
        var image = Decode(Concat(Header(colorMapType: 0, mapLength: 300, entryBits: 24, descriptor: 0x20), [1, 2, 3]));
        Assert.Equal(0xFF030201u, image.GetPixel(0, 0));
        Assert.True(HasWarning(image, TgaWarningCode.ColorMapFieldsIgnored));
    }

    [Fact]
    public void ReservedColorMapType_WithoutData_IsIgnored()
    {
        var image = Decode(Concat(Header(colorMapType: 9, descriptor: 0x20), [1, 2, 3]));
        Assert.Equal(0xFF030201u, image.GetPixel(0, 0));
        Assert.True(HasWarning(image, TgaWarningCode.ReservedColorMapTypeIgnored));
    }

    [Fact]
    public void Rle_RunOfInvalidIndices_IsMarked()
    {
        var file = Concat(Header(colorMapType: 1, imageType: 9, mapLength: 1, entryBits: 24, width: 3, depth: 8, descriptor: 0x20), [1, 2, 3], [0x82, 7]);
        var image = Decode(file);
        Assert.Equal(3, image.InvalidIndexPixels!.CountSet());
        Assert.True(HasWarning(image, TgaWarningCode.InvalidPaletteIndices));
    }

    [Fact]
    public void Cancellation_IsHonoured()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        byte[] file = LargeTga.Uncompressed32(64, 64);
        Assert.ThrowsAny<OperationCanceledException>(() => TgaDecoder.Decode(file, null, cancelled.Token));
    }

    [Fact]
    public void NonSeekableStreams_DecodeWithoutMetadata()
    {
        var spec = TgaSamples.TrueColor(24, origin: 2, width: 5, height: 4);
        spec.Tga2 = new Tga2Spec { AuthorName = "x" };
        using var stream = new ForwardOnlyStream(TgaBuilder.Bytes(spec));
        var image = TgaDecoder.Decode(stream);
        Assert.Equal(TgaPixels.ExpectedImage(spec), Pixels(image));
        Assert.False(image.Metadata.HasFooter);
    }

    private sealed class ForwardOnlyStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
