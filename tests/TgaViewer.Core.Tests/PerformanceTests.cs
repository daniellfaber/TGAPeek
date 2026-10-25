using System.Diagnostics;
using TgaViewer.TestSupport;
using Xunit.Abstractions;

namespace TgaViewer.Core.Tests;

/// <summary>
/// Representative timings, printed to the test output. The limits only catch pathological slowness;
/// the numbers are reported, not tuned to pass.
/// </summary>
public class PerformanceTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Performance")]
    public void DecodeAndRender_LargeImages()
    {
        const int size = 4096;
        byte[] uncompressed = LargeTga.Uncompressed32(size, size);
        byte[] rle = LargeTga.Rle32(size, size);
        TgaDecoder.Decode(LargeTga.Rle32(64, 64)); // warm up
        Measure($"Decode {size} × {size} uncompressed 32-bit ({ByteSize.Format(uncompressed.Length)} file)", () => TgaDecoder.Decode(uncompressed), out var image, limitSeconds: 30);
        Measure($"Decode {size} × {size} RLE 32-bit ({ByteSize.Format(rle.Length)} file)", () => TgaDecoder.Decode(rle), out _, limitSeconds: 30);

        var buffer = new uint[1920 * 1080];
        var fit = ViewMath.Fit(size, size, 1920, 1080);
        var actual = ViewMath.Centre(1, size, size, 1920, 1080);
        var magnified = ViewMath.Centre(4, size, size, 1920, 1080);
        foreach (var (name, view) in new[] { ("fit", fit), ("100%", actual), ("400%", magnified) })
        {
            foreach (var scaling in Enum.GetValues<ScalingMode>())
            {
                var options = new DisplayOptions { Scaling = scaling };
                DisplayRenderer.Render(image, AlphaInterpretation.Straight, options, view, buffer, 1920, 1080, 1920); // warm up
                Measure($"Render 1920 × 1080 viewport, {name}, {scaling}", () =>
                {
                    DisplayRenderer.Render(image, AlphaInterpretation.Straight, options, view, buffer, 1920, 1080, 1920);
                    return image;
                }, out _, limitSeconds: 5);
            }
        }

        output.WriteLine($"Decoded image buffers: {ByteSize.Format(image.MemoryBytes)}; viewport buffers: {ByteSize.Format(TgaMemoryEstimator.EstimateViewportBytes(1920, 1080))}.");
    }

    private void Measure(string name, Func<TgaImage> action, out TgaImage result, double limitSeconds)
    {
        var watch = Stopwatch.StartNew();
        result = action();
        watch.Stop();
        output.WriteLine($"{name}: {watch.Elapsed.TotalMilliseconds:N0} ms");
        Assert.True(watch.Elapsed.TotalSeconds < limitSeconds, $"{name} took {watch.Elapsed.TotalSeconds:N1} s.");
    }
}
