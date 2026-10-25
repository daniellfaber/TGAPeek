using TGAPeek.TestSupport;
using static TGAPeek.Core.Tests.Helpers;

namespace TGAPeek.Core.Tests;

public class DisplayRendererTests
{
    private const uint Light = 0xFFFFFFFF;
    private const uint Dark = 0xFFCCCCCC;
    private static readonly DisplayOptions Checker1 = new() { CheckerSize = 1 };

    private static uint Rgb(int r, int g, int b) => 0xFF000000 | (uint)r << 16 | (uint)g << 8 | (uint)b;

    /// <summary>A top-left 32-bit image with the given 0xAARRGGBB values.</summary>
    private static TgaImage Image(int width, int height, params uint[] argb) => Decode(new TgaSpec
    {
        PixelDepth = 32,
        AttributeBits = 8,
        TopToBottom = true,
        Width = width,
        Height = height,
        Pixels = argb.Select(p => TgaPixels.Bgra((int)(p >> 16) & 0xFF, (int)(p >> 8) & 0xFF, (int)p & 0xFF, (int)(p >> 24))).ToArray(),
    });

    private static uint[] Render(TgaImage image, AlphaInterpretation alpha, DisplayOptions options, ViewTransform view, int width, int height, bool parallel = true)
    {
        var buffer = new uint[width * height];
        DisplayRenderer.Render(image, alpha, options, view, buffer, width, height, width, parallel);
        return buffer;
    }

    // Independent reference: straight compositing in double precision, rounded to nearest.
    private static uint ReferenceStraight(uint c, uint a, uint bg) => (uint)Math.Round((c * a + bg * (255.0 - a)) / 255.0, MidpointRounding.AwayFromZero);

    [Fact]
    public void Straight_MatchesTheReferenceForEveryAlpha()
    {
        foreach (uint a in Enumerable.Range(0, 256).Select(i => (uint)i))
        {
            foreach (uint c in new uint[] { 0, 1, 77, 128, 200, 255 })
            {
                foreach (uint bg in new uint[] { 0, 128, 255 })
                {
                    uint pixel = a << 24 | c << 16 | c << 8 | c;
                    uint expected = ReferenceStraight(c, a, bg);
                    Assert.Equal(Rgb((int)expected, (int)expected, (int)expected), DisplayRenderer.CompositeStraight(pixel, Rgb((int)bg, (int)bg, (int)bg)));
                }
            }
        }
    }

    [Fact]
    public void Premultiplied_MatchesTheReferenceForEveryAlpha()
    {
        foreach (uint a in Enumerable.Range(0, 256).Select(i => (uint)i))
        {
            for (uint c = 0; c <= 255; c += 17)
            {
                foreach (uint bg in new uint[] { 0, 99, 255 })
                {
                    uint pixel = a << 24 | c << 16 | c << 8 | c;
                    uint expected = Math.Min(c, a) + (uint)Math.Round(bg * (255.0 - a) / 255.0, MidpointRounding.AwayFromZero);
                    Assert.Equal(Rgb((int)expected, (int)expected, (int)expected), DisplayRenderer.CompositePremultiplied(pixel, Rgb((int)bg, (int)bg, (int)bg)));
                }
            }
        }
    }

    [Fact]
    public void WorkedExamples()
    {
        // Semi-transparent (a = 128) over white.
        Assert.Equal(Rgb(227, 177, 152), DisplayRenderer.CompositeStraight(0x80C86432, Rgb(255, 255, 255)));
        Assert.Equal(Rgb(227, 177, 127), DisplayRenderer.CompositePremultiplied(0x80643200, Rgb(255, 255, 255)));

        // Invalid premultiplied data (red above alpha): red is clamped to alpha for display.
        Assert.Equal(Rgb(255, 177, 127), DisplayRenderer.CompositePremultiplied(0x80C83200, Rgb(255, 255, 255)));
        Assert.Equal(Rgb(10, 10, 10), DisplayRenderer.CompositePremultiplied(0x0AFAF0E6, Rgb(0, 0, 0)));

        // Boundary c = a is valid; a = 255 is always shown as stored.
        Assert.Equal(Rgb(128, 128, 128), DisplayRenderer.CompositePremultiplied(0x80808080, Rgb(0, 0, 0)));
        Assert.Equal(Rgb(1, 2, 3), DisplayRenderer.CompositePremultiplied(0xFF010203, Rgb(9, 9, 9)));
        Assert.Equal(Rgb(1, 2, 3), DisplayRenderer.CompositeStraight(0xFF010203, Rgb(9, 9, 9)));
    }

    [Fact]
    public void FullyTransparentPixels_WithColour_ShowExactlyTheBackground()
    {
        uint transparentColour = 0x00C86432; // a = 0, RGB (200, 100, 50)
        foreach (uint background in new[] { Rgb(17, 34, 51), Light, Dark, Rgb(0, 0, 0), Rgb(128, 128, 128) })
        {
            Assert.Equal(background, DisplayRenderer.CompositeStraight(transparentColour, background));
            Assert.Equal(background, DisplayRenderer.CompositePremultiplied(transparentColour, background));
        }
    }

    [Fact]
    public void InvalidPremultipliedPixels_AreDetectedAndCounted()
    {
        Assert.True(DisplayRenderer.IsValidPremultiplied(0x80808080));
        Assert.False(DisplayRenderer.IsValidPremultiplied(0x80818080));
        Assert.False(DisplayRenderer.IsValidPremultiplied(0x00010000));
        var image = Image(3, 1, 0x80808080, 0x80FF0000, 0x00000001);
        Assert.Equal(2, image.CountInvalidPremultipliedPixels());
    }

    [Theory]
    [InlineData(AlphaInterpretation.Straight)]
    [InlineData(AlphaInterpretation.Premultiplied)]
    public void Render_OverCheckerboard_UsesTheRightCellForEachPixel(AlphaInterpretation alpha)
    {
        uint[] pixels = [0x80C86432, 0x00C86432, 0xFF102030, 0x40404040];
        var image = Image(2, 2, pixels);
        uint[] output = Render(image, alpha, Checker1, new ViewTransform(1, 0, 0), 2, 2);
        uint[] backgrounds = [Light, Dark, Dark, Light];
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(DisplayRenderer.Composite(pixels[i], alpha, backgrounds[i]), output[i]);
        }
    }

    [Theory]
    [InlineData(3.0, -5.0, 4)]
    [InlineData(-7.0, 2.0, 3)]
    [InlineData(0.0, 0.0, 8)]
    public void Checkerboard_StaysAttachedToTheImageWhenPanned(double originX, double originY, int cell)
    {
        var image = Image(1, 1, 0x00000000);
        var options = new DisplayOptions { CheckerSize = cell };
        const int size = 64;
        uint[] output = Render(image, AlphaInterpretation.Straight, options, new ViewTransform(100, originX, originY), size, size);
        for (int sy = 0; sy < size; sy++)
        {
            for (int sx = 0; sx < size; sx++)
            {
                if (sx + 0.5 < originX || sy + 0.5 < originY)
                {
                    continue;
                }

                bool even = ((int)Math.Floor((sx - Math.Floor(originX)) / cell) + (int)Math.Floor((sy - Math.Floor(originY)) / cell)) % 2 == 0;
                Assert.Equal(even ? Light : Dark, output[sy * size + sx]);
            }
        }
    }

    [Fact]
    public void Zoom_And_Pan_MapScreenPixelsToImagePixels()
    {
        var image = Image(2, 2, Rgb(1, 0, 0), Rgb(2, 0, 0), Rgb(3, 0, 0), Rgb(4, 0, 0));
        var options = new DisplayOptions { CanvasColor = 0xFF123456 };

        uint[] zoomed = Render(image, AlphaInterpretation.Opaque, options, new ViewTransform(3, 0, 0), 7, 7);
        Assert.Equal(Rgb(1, 0, 0), zoomed[0]);
        Assert.Equal(Rgb(1, 0, 0), zoomed[2 * 7 + 2]);
        Assert.Equal(Rgb(4, 0, 0), zoomed[4 * 7 + 4]);
        Assert.Equal(0xFF123456u, zoomed[6 * 7 + 6]);

        uint[] panned = Render(image, AlphaInterpretation.Opaque, options, new ViewTransform(1, -1, 0), 2, 2);
        Assert.Equal(new[] { Rgb(2, 0, 0), 0xFF123456u, Rgb(4, 0, 0), 0xFF123456u }, panned);
    }

    [Fact]
    public void SolidBackground_IsUsedWhenTheCheckerboardIsOff()
    {
        var image = Image(1, 1, 0x00FFFFFF);
        var options = new DisplayOptions { Checkerboard = false, SolidBackground = Rgb(10, 20, 30) };
        Assert.Equal(Rgb(10, 20, 30), Render(image, AlphaInterpretation.Straight, options, new ViewTransform(1, 0, 0), 1, 1)[0]);
    }

    [Fact]
    public void ChannelViews_ShowStoredValues()
    {
        var image = Image(1, 1, 0x40C86432);
        var view = new ViewTransform(1, 0, 0);
        uint Show(ChannelView channel) => Render(image, AlphaInterpretation.Straight, new DisplayOptions { Channel = channel }, view, 1, 1)[0];

        Assert.Equal(Rgb(200, 200, 200), Show(ChannelView.Red));
        Assert.Equal(Rgb(100, 100, 100), Show(ChannelView.Green));
        Assert.Equal(Rgb(50, 50, 50), Show(ChannelView.Blue));
        Assert.Equal(Rgb(64, 64, 64), Show(ChannelView.Alpha));
        Assert.Equal(Rgb(200, 100, 50), Show(ChannelView.Rgb));
    }

    [Fact]
    public void AlphaView_OfAnImageWithoutAlpha_ShowsTheNoAlphaPattern()
    {
        var image = Decode(new TgaSpec { Width = 8, Height = 8, Pixels = Enumerable.Repeat(TgaPixels.Bgr(9, 9, 9), 64).ToArray() });
        uint[] output = Render(image, AlphaInterpretation.Opaque, new DisplayOptions { Channel = ChannelView.Alpha }, new ViewTransform(1, 0, 0), 8, 8);
        Assert.All(output, p => Assert.Contains(p, new[] { DisplayRenderer.NoAlphaStripe1, DisplayRenderer.NoAlphaStripe2 }));
    }

    [Fact]
    public void MissingAndInvalidPixels_AreHatched()
    {
        byte[] truncated = Concat(Header(width: 4, height: 4, descriptor: 0x20), Enumerable.Repeat((byte)7, 3 * 6).ToArray());
        var partial = Decode(truncated, new TgaDecodeOptions { AllowPartial = true });
        uint[] output = Render(partial, AlphaInterpretation.Opaque, new DisplayOptions(), new ViewTransform(1, 0, 0), 4, 4);
        Assert.Equal(Rgb(7, 7, 7), output[0]);
        Assert.Contains(output[15], new[] { DisplayRenderer.MissingStripe1, DisplayRenderer.MissingStripe2 });

        var invalid = Decode(Concat(Header(colorMapType: 1, imageType: 1, mapLength: 1, entryBits: 24, width: 2, depth: 8, descriptor: 0x20), [1, 2, 3], [0, 9]));
        uint[] marked = Render(invalid, AlphaInterpretation.Opaque, new DisplayOptions(), new ViewTransform(1, 0, 0), 2, 1);
        Assert.Equal(Rgb(3, 2, 1), marked[0]);
        Assert.Contains(marked[1], new[] { DisplayRenderer.InvalidStripe1, DisplayRenderer.InvalidStripe2 });
    }

    [Fact]
    public void Rendering_NeverChangesTheStoredPixels()
    {
        var image = Image(3, 2, 0x80C86432, 0x00FF0000, 0xFFFFFFFF, 0x10203040, 0x7F7F7F7F, 0x80FF00FF);
        uint[] before = Pixels(image);
        foreach (var alpha in Enum.GetValues<AlphaInterpretation>())
        {
            foreach (var channel in Enum.GetValues<ChannelView>())
            {
                foreach (var scaling in Enum.GetValues<ScalingMode>())
                {
                    foreach (double zoom in new[] { 0.3, 1, 2.5 })
                    {
                        Render(image, alpha, new DisplayOptions { Channel = channel, Scaling = scaling }, new ViewTransform(zoom, 1.5, -0.5), 9, 7);
                    }
                }
            }
        }

        Assert.Equal(before, Pixels(image));
    }

    [Theory]
    [InlineData(2.5)]
    [InlineData(0.3)]
    public void Smooth_KeepsUniformImagesUniform(double zoom)
    {
        var image = Image(4, 4, Enumerable.Repeat(Rgb(12, 34, 56), 16).ToArray());
        var view = ViewMath.Centre(zoom, 4, 4, 20, 20);
        uint[] output = Render(image, AlphaInterpretation.Opaque, new DisplayOptions { Scaling = ScalingMode.Smooth, CanvasColor = 0xFF000001 }, view, 20, 20);
        Assert.All(output.Where(p => p != 0xFF000001), p => Assert.Equal(Rgb(12, 34, 56), p));
    }

    [Fact]
    public void Smooth_DoesNotBleedTheColourOfTransparentPixels()
    {
        // Opaque red next to fully transparent green, over white: blended pixels must stay red/white (R = 255),
        // which is only true when filtering uses premultiplied colour.
        var image = Image(2, 1, 0xFFFF0000, 0x0000FF00);
        var options = new DisplayOptions { Scaling = ScalingMode.Smooth, Checkerboard = false, SolidBackground = Light };
        uint[] output = Render(image, AlphaInterpretation.Straight, options, new ViewTransform(8, 0, 0), 16, 1);
        Assert.All(output, p => Assert.Equal(255u, (p >> 16) & 0xFF));
    }

    [Fact]
    public void ParallelAndSerialRendering_Agree()
    {
        var spec = RandomSpec(2, 32, 0, 0, new Random(9), 50, 40);
        spec.AttributeBits = 8;
        var image = Decode(spec);
        foreach (var scaling in Enum.GetValues<ScalingMode>())
        {
            var options = new DisplayOptions { Scaling = scaling };
            var view = new ViewTransform(2.3, -7.4, 3.2);
            Assert.Equal(
                Render(image, AlphaInterpretation.Straight, options, view, 130, 97, parallel: false),
                Render(image, AlphaInterpretation.Straight, options, view, 130, 97, parallel: true));
        }
    }

    [Fact]
    public void InvalidArguments_AreRejected()
    {
        var image = Image(1, 1, 0xFFFFFFFF);
        Assert.Throws<ArgumentException>(() => DisplayRenderer.Render(image, AlphaInterpretation.Opaque, new DisplayOptions(), new ViewTransform(1, 0, 0), new uint[3], 2, 2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayRenderer.Render(image, AlphaInterpretation.Opaque, new DisplayOptions(), new ViewTransform(0, 0, 0), new uint[4], 2, 2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayRenderer.Render(image, AlphaInterpretation.Opaque, new DisplayOptions(), new ViewTransform(1, double.NaN, 0), new uint[4], 2, 2, 2));
    }
}
