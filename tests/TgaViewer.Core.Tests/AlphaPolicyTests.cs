using TgaViewer.TestSupport;
using static TgaViewer.Core.Tests.Helpers;

namespace TgaViewer.Core.Tests;

public class AlphaPolicyTests
{
    private static TgaImage TrueColor(int depth, int attributeBits, int alpha = 128, byte? attributesType = null)
    {
        var spec = new TgaSpec
        {
            PixelDepth = (byte)depth,
            AttributeBits = attributeBits,
            Width = 2,
            Pixels = depth switch
            {
                32 => [TgaPixels.Bgra(10, 20, 30, alpha), TgaPixels.Bgra(40, 50, 60, alpha)],
                16 => [TgaPixels.Word(1, 2, 3, alpha != 0), TgaPixels.Word(4, 5, 6, false)],
                _ => [TgaPixels.Bgr(10, 20, 30), TgaPixels.Bgr(40, 50, 60)],
            },
        };
        if (attributesType is byte type)
        {
            spec.Tga2 = new Tga2Spec { AttributesType = type };
        }

        return Decode(spec);
    }

    private static AlphaInterpretation Resolve(TgaImage image, AlphaMode mode = AlphaMode.Auto) => AlphaPolicy.Resolve(image, mode).Interpretation;

    [Fact]
    public void NoStoredAlpha_IsAlwaysOpaque()
    {
        var image = TrueColor(24, 0);
        Assert.Equal(AlphaInterpretation.Opaque, Resolve(image));
        Assert.Equal(AlphaInterpretation.Opaque, Resolve(image, AlphaMode.Use));
        Assert.Contains("no alpha", AlphaPolicy.Resolve(image, AlphaMode.Auto).Explanation);
    }

    [Fact]
    public void DeclaredAlphaBits_AreApplied() => Assert.Equal(AlphaInterpretation.Straight, Resolve(TrueColor(32, 8)));

    [Fact]
    public void ThirtyTwoBitsAlone_DoNotImplyAlpha()
    {
        var image = TrueColor(32, 0);
        var decision = AlphaPolicy.Resolve(image, AlphaMode.Auto);
        Assert.Equal(AlphaInterpretation.Opaque, decision.Interpretation);
        Assert.Contains("not applied", decision.Summary);
        Assert.Equal(AlphaInterpretation.Straight, Resolve(image, AlphaMode.Use));
    }

    [Fact]
    public void AllZeroAlpha_IsShownOpaque_ButCanBeForced()
    {
        var image = TrueColor(32, 8, alpha: 0);
        Assert.True(image.Alpha.AllZero);
        Assert.Equal(AlphaInterpretation.Opaque, Resolve(image));
        Assert.Equal(AlphaInterpretation.Straight, Resolve(image, AlphaMode.Use));
        Assert.Equal(0u, image.GetPixel(0, 0) >> 24);
    }

    [Fact]
    public void Ignore_AlwaysWins() => Assert.Equal(AlphaInterpretation.Opaque, Resolve(TrueColor(32, 8), AlphaMode.Ignore));

    [Theory]
    [InlineData((byte)0, AlphaInterpretation.Opaque)]
    [InlineData((byte)1, AlphaInterpretation.Opaque)]
    [InlineData((byte)2, AlphaInterpretation.Opaque)]
    [InlineData((byte)3, AlphaInterpretation.Straight)]
    [InlineData((byte)4, AlphaInterpretation.Premultiplied)]
    public void AttributesType_DecidesAuto(byte type, AlphaInterpretation expected)
    {
        Assert.Equal(expected, Resolve(TrueColor(32, 8, attributesType: type)));
        Assert.Equal(expected, Resolve(TrueColor(32, 0, attributesType: type)));
    }

    [Fact]
    public void PremultipliedFiles_StayPremultipliedWhenForced() =>
        Assert.Equal(AlphaInterpretation.Premultiplied, Resolve(TrueColor(32, 8, attributesType: 4), AlphaMode.Use));

    [Fact]
    public void RetainedUndefinedAlpha_IsKeptButNotApplied()
    {
        var image = TrueColor(32, 8, attributesType: 2);
        Assert.Equal(AlphaInterpretation.Opaque, Resolve(image));
        Assert.Equal(128u, image.GetPixel(0, 0) >> 24);
        Assert.Contains("retained", AlphaPolicy.Resolve(image, AlphaMode.Auto).Explanation);
    }

    [Fact]
    public void SixteenBit_UsesTheAttributeBitOnlyWhenDeclared()
    {
        Assert.Equal(AlphaInterpretation.Straight, Resolve(TrueColor(16, 1)));
        Assert.Equal(AlphaInterpretation.Opaque, Resolve(TrueColor(16, 0)));
    }

    [Fact]
    public void Greyscale16_AndColourMappedEntries()
    {
        var grey = Decode(new TgaSpec { ImageType = 3, PixelDepth = 16, AttributeBits = 8, Pixels = [[50, 60]] });
        Assert.Equal(AlphaInterpretation.Straight, Resolve(grey));

        var mapped32 = Decode(new TgaSpec { ImageType = 1, PixelDepth = 8, AttributeBits = 8, ColorMapEntryBits = 32, ColorMap = [TgaPixels.Bgra(1, 2, 3, 100)], Pixels = [[0]] });
        Assert.Equal(AlphaInterpretation.Straight, Resolve(mapped32));

        var mapped24 = Decode(new TgaSpec { ImageType = 1, PixelDepth = 8, ColorMapEntryBits = 24, ColorMap = [TgaPixels.Bgr(1, 2, 3)], Pixels = [[0]] });
        Assert.Equal(TgaAlphaStorage.None, mapped24.Alpha.Storage);
        Assert.Equal(AlphaInterpretation.Opaque, Resolve(mapped24));
    }
}
