using System.Diagnostics;
using TgaViewer.TestSupport;
using Xunit.Abstractions;
using static TgaViewer.Core.Tests.Helpers;

namespace TgaViewer.Core.Tests;

/// <summary>
/// Seeded random damage to valid files. Guarantees checked: the decoder returns an image or throws a
/// <see cref="TgaException"/>, nothing else; it finishes quickly; and returned images are consistent.
/// </summary>
public class FuzzTests(ITestOutputHelper output)
{
    private static readonly TgaDecodeOptions Lenient = new() { AllowPartial = true, MaxDecodeMemoryBytes = 16L << 20 };
    private static readonly TgaDecodeOptions Strict = Lenient with { AllowPartial = false };

    private static List<(string Name, byte[] Bytes)> Fixtures()
    {
        var tga2 = TgaSamples.TrueColor(32, origin: 1, attributeBits: 8, width: 9, height: 5);
        tga2.ImageId = "fuzz"u8.ToArray();
        tga2.Tga2 = new Tga2Spec
        {
            AuthorName = "fuzz",
            AttributesType = 4,
            ColorCorrectionTable = new ushort[1024],
            StampWidth = 2,
            StampHeight = 2,
            StampPixels = [TgaPixels.Bgra(1, 2, 3, 4), TgaPixels.Bgra(5, 6, 7, 8), TgaPixels.Bgra(9, 10, 11, 12), TgaPixels.Bgra(13, 14, 15, 16)],
            IncludeScanLineTable = true,
            DeveloperFields = [(1, [1, 2, 3]), (2, [4, 5])],
        };

        return
        [
            ("TGA 2.0 32-bit", TgaBuilder.Bytes(tga2)),
            ("RLE colour-mapped 16-bit", TgaBuilder.Bytes(RandomColorMapped(new Random(1)))),
            ("RLE greyscale four-way", TgaBuilder.Bytes(RandomSpec(11, 8, 0, 2, new Random(2), 6, 11))),
            ("RLE 15-bit two-way", TgaBuilder.Bytes(RandomSpec(10, 15, 3, 1, new Random(3), 8, 7))),
            ("24-bit bottom-right", TgaBuilder.Bytes(RandomSpec(2, 24, 1, 0, new Random(4), 10, 6))),
            ("greyscale + alpha", TgaBuilder.Bytes(RandomSpec(3, 16, 2, 0, new Random(5), 7, 7))),
        ];
    }

    private static TgaSpec RandomColorMapped(Random random)
    {
        var spec = RandomSpec(9, 16, 2, 0, random, 8, 6, r => TgaPixels.Index(500 + r.Next(-2, 12), 16));
        spec.ColorMapFirstIndex = 500;
        spec.ColorMapEntryBits = 16;
        spec.ColorMap = Enumerable.Range(0, 10).Select(i => TgaPixels.Word(i, i, i, i % 2 == 0)).ToArray();
        return spec;
    }

    [Fact]
    public void RandomDamage_NeverEscapesTheDecoder()
    {
        var fixtures = Fixtures();
        var random = new Random(20261025);
        const int iterations = 6000;
        int decoded = 0, rejected = 0;
        long slowest = 0;
        var watch = new Stopwatch();
        for (int i = 0; i < iterations; i++)
        {
            var (name, original) = fixtures[i % fixtures.Count];
            byte[] data = Mutate(original, random);
            watch.Restart();
            try
            {
                var image = TgaDecoder.Decode(data, i % 2 == 0 ? Lenient : Strict);
                CheckConsistency(image);
                decoded++;
            }
            catch (TgaException)
            {
                rejected++;
            }
            catch (Exception ex)
            {
                Assert.Fail($"Iteration {i} ({name}) threw {ex.GetType().Name}: {ex.Message}\nInput: {Convert.ToHexString(data)}");
            }

            slowest = Math.Max(slowest, watch.ElapsedMilliseconds);
        }

        output.WriteLine($"{iterations} damaged files: {decoded} decoded, {rejected} rejected with TgaException, slowest {slowest} ms.");
        Assert.True(slowest < 2000, $"The slowest decode took {slowest} ms.");
        Assert.True(decoded > 0 && rejected > 0);
    }

    [Fact]
    public void RandomHeaders_NeverEscapeTheDecoder()
    {
        var random = new Random(7);
        for (int i = 0; i < 4000; i++)
        {
            var data = new byte[18 + random.Next(0, 300)];
            random.NextBytes(data);
            data[2] = (byte)new[] { 1, 2, 3, 9, 10, 11, random.Next(256) }[random.Next(7)];
            data[13] = (byte)random.Next(2);
            data[15] = (byte)random.Next(2);
            try
            {
                CheckConsistency(TgaDecoder.Decode(data, Lenient));
            }
            catch (TgaException)
            {
            }
            catch (Exception ex)
            {
                Assert.Fail($"Iteration {i} threw {ex.GetType().Name}: {ex.Message}\nInput: {Convert.ToHexString(data)}");
            }
        }
    }

    private static byte[] Mutate(byte[] original, Random random)
    {
        byte[] data = (byte[])original.Clone();
        switch (random.Next(6))
        {
            case 0:
                for (int n = random.Next(1, 9); n > 0; n--)
                {
                    data[random.Next(data.Length)] ^= (byte)(1 << random.Next(8));
                }

                return data;
            case 1:
                byte[] interesting = [0, 1, 0x7F, 0x80, 0xFE, 0xFF];
                for (int n = random.Next(1, 5); n > 0; n--)
                {
                    data[random.Next(data.Length)] = random.Next(3) == 0 ? (byte)random.Next(256) : interesting[random.Next(interesting.Length)];
                }

                return data;
            case 2:
                return data[..random.Next(data.Length)];
            case 3:
                data[random.Next(18)] = (byte)random.Next(256);
                return data;
            case 4:
                int at = random.Next(data.Length);
                int count = random.Next(1, 16);
                return random.Next(2) == 0
                    ? [.. data[..at], .. Enumerable.Range(0, count).Select(_ => (byte)random.Next(256)), .. data[at..]]
                    : [.. data[..at], .. data[Math.Min(data.Length, at + count)..]];
            default:
                int start = Math.Max(0, data.Length - 600);
                for (int n = random.Next(1, 4); n > 0; n--)
                {
                    int position = random.Next(start, Math.Max(start + 1, data.Length - 4));
                    uint value = random.Next(3) switch { 0 => (uint)random.Next(), 1 => (uint)random.Next(data.Length), _ => 0xFFFFFFFF };
                    for (int b = 0; b < 4 && position + b < data.Length; b++)
                    {
                        data[position + b] = (byte)(value >> (8 * b));
                    }
                }

                return data;
        }
    }

    private static void CheckConsistency(TgaImage image)
    {
        Assert.True(image.DecodedPixelCount <= image.PixelCount);
        if (image.IsPartial)
        {
            Assert.Equal(image.PixelCount - image.DecodedPixelCount, image.MissingPixels!.CountSet());
        }
        else
        {
            Assert.Equal(image.PixelCount, image.DecodedPixelCount);
        }

        for (int y = 0; y < image.Height; y++)
        {
            Assert.Equal(image.Width, image.GetRow(y).Length);
        }

        _ = ImageReport.Build(image, null, AlphaPolicy.Resolve(image, AlphaMode.Auto));
        _ = Pixels(image);
    }
}
