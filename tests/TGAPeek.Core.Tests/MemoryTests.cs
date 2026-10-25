using TGAPeek.TestSupport;

namespace TGAPeek.Core.Tests;

public class MemoryTests
{
    private static readonly TgaDecodeOptions Partial = new() { AllowPartial = true };

    public static IEnumerable<object[]> Fixtures()
    {
        yield return ["512 × 512 RLE 32-bit", LargeTga.Rle32(512, 512)];
        yield return ["300 × 200 uncompressed 32-bit", LargeTga.Uncompressed32(300, 200)];
        yield return ["colour-mapped, 16-bit indices", TgaBuilder.Bytes(TgaSamples.ColorMapped(16, 32, firstIndex: 300, attributeBits: 8))];
        yield return ["invalid indices (mask allocated)", TgaSamples.All().First(s => s.Name.StartsWith("24 ")).Bytes];
        yield return ["truncated (missing mask allocated)", LargeTga.Uncompressed32(200, 100)[..30_000]];
        yield return ["TGA 2.0 with every structure", TgaSamples.All().First(s => s.Name.StartsWith("21 ")).Bytes];
        yield return ["4096 × 1", LargeTga.Uncompressed32(4096, 1)];
        yield return ["1 × 4096", LargeTga.Uncompressed32(1, 4096)];
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ActualAllocations_StayWithinTheEstimate(string name, byte[] file)
    {
        using var stream = new MemoryStream(file, writable: false);
        var probe = TgaDecoder.Probe(stream);
        TgaDecoder.Decode(file, Partial); // warm up so one-time runtime allocations are not counted

        stream.Position = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        var image = TgaDecoder.Decode(stream, Partial);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated <= probe.Memory.Total, $"{name}: allocated {allocated:N0} bytes; estimate {probe.Memory.Total:N0}");
        Assert.True(image.MemoryBytes >= image.PixelCount * 4);
    }

    [Fact]
    public void Estimate_CoversEachComponent()
    {
        var trueColor = TgaMemoryEstimator.Estimate(new TgaHeader { ImageType = 2, Width = 100, Height = 50, PixelDepth = 24 });
        Assert.True(trueColor.PixelBytes >= 100 * 50 * 4);
        Assert.Equal(0, trueColor.IndexBytes);
        Assert.Equal(0, trueColor.PaletteBytes);
        Assert.True(trueColor.MaskBytes >= 2 * (100 * 50 / 8));
        Assert.True(trueColor.Total > trueColor.Subtotal);

        var colorMapped = TgaMemoryEstimator.Estimate(new TgaHeader { ImageType = 1, ColorMapType = 1, ColorMapLength = 300, ColorMapEntryBits = 32, Width = 100, Height = 50, PixelDepth = 16 });
        Assert.True(colorMapped.IndexBytes >= 2 * 100 * 50);
        Assert.True(colorMapped.PaletteBytes >= 300 * 4);
    }

    [Fact]
    public void LargestPossibleImage_IsEstimatedWithoutOverflow()
    {
        var estimate = TgaMemoryEstimator.Estimate(new TgaHeader { ImageType = 1, ColorMapType = 1, ColorMapLength = 65535, ColorMapEntryBits = 32, Width = 65535, Height = 65535, PixelDepth = 16 });
        Assert.True(estimate.Total > 65535L * 65535 * 6);
    }

    [Fact]
    public void ViewportEstimate_IsEightBytesPerScreenPixel()
    {
        Assert.Equal(1920L * 1080 * 8, TgaMemoryEstimator.EstimateViewportBytes(1920, 1080));
        Assert.Equal(0, TgaMemoryEstimator.EstimateViewportBytes(-5, 10));
    }
}
