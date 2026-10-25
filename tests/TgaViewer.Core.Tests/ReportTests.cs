using TgaViewer.TestSupport;
using static TgaViewer.Core.Tests.Helpers;

namespace TgaViewer.Core.Tests;

public class ReportTests
{
    private static TgaImage Sample(string prefix) =>
        TgaDecoder.Decode(TgaSamples.All().First(s => s.Name.StartsWith(prefix)).Bytes, new TgaDecodeOptions { AllowPartial = true });

    [Fact]
    public void ImageReport_DescribesTga2Metadata()
    {
        var image = Sample("21 ");
        string report = ImageReport.Build(image, @"C:\textures\sample.tga", AlphaPolicy.Resolve(image, AlphaMode.Auto));
        Assert.Contains("TGA 2.0", report);
        Assert.Contains("sample.tga", report);
        Assert.Contains("premultiplied", report);
        Assert.Contains("Gamma", report);
        Assert.Contains("not applied", report);
        Assert.Contains("Tag 1000", report);
        Assert.Contains("8 × 6 pixels", report);
    }

    [Fact]
    public void ImageReport_ExplainsPartialImages()
    {
        var image = Sample("23 ");
        string report = ImageReport.Build(image, null, AlphaPolicy.Resolve(image, AlphaMode.Auto));
        Assert.Contains("partial", report);
        Assert.Contains("Warning:", report);
    }

    [Fact]
    public void PixelInspector_ReportsStoredValues()
    {
        var sixteen = Decode(new TgaSpec { PixelDepth = 16, AttributeBits = 1, Pixels = [TgaPixels.Word(31, 1, 0, attribute: true)] });
        string text = PixelInspector.Describe(sixteen, 0, 0, AlphaPolicy.Resolve(sixteen, AlphaMode.Auto));
        Assert.Contains("0x FC20".Replace(" ", ""), text);
        Assert.Contains("5-bit R 31, G 1, B 0", text);
        Assert.Contains("attribute bit 1", text);

        var mapped = Sample("12 ");
        Assert.Contains("Palette index 16", PixelInspector.Describe(mapped, 0, 0, AlphaPolicy.Resolve(mapped, AlphaMode.Auto)));

        var noAlpha = Sample("01 ");
        Assert.Contains("not stored", PixelInspector.Describe(noAlpha, 0, 0, AlphaPolicy.Resolve(noAlpha, AlphaMode.Auto)));
        Assert.Contains("R 0 G 0 B 0", PixelInspector.Summarise(noAlpha, 0, 0));
    }

    [Fact]
    public void PixelInspector_ReportsMissingAndInvalidPixels()
    {
        var partial = Sample("23 ");
        Assert.Contains("Missing", PixelInspector.Describe(partial, 63, 47, AlphaPolicy.Resolve(partial, AlphaMode.Auto)));

        var invalid = Sample("24 ");
        Assert.Contains("outside the colour map", PixelInspector.Describe(invalid, 30, 20, AlphaPolicy.Resolve(invalid, AlphaMode.Auto)));
        Assert.Contains("invalid palette index 200", PixelInspector.Summarise(invalid, 30, 20));
    }

    [Fact]
    public void PixelInspector_DerivesUnpremultipliedValues()
    {
        var image = Sample("21 ");
        string text = PixelInspector.Describe(image, 10, 10, AlphaPolicy.Resolve(image, AlphaMode.Auto));
        Assert.Contains("Unpremultiplied (derived, not stored)", text);
    }

    [Fact]
    public void Text_IsMadeSafeForDisplay()
    {
        Assert.Equal("ab?c", TgaText.Field([(byte)'a', (byte)'b', 0x07, (byte)'c', 0, (byte)'z']));
        Assert.Equal("(none)", TgaText.Describe([]));
        Assert.StartsWith("3 bytes: 00 FF 10", TgaText.Describe([0x00, 0xFF, 0x10]));
    }
}
