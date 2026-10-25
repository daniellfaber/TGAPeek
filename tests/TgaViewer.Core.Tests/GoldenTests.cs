using TgaViewer.TestSupport;
using static TgaViewer.Core.Tests.Helpers;

namespace TgaViewer.Core.Tests;

/// <summary>
/// Files written byte by byte from the TGA specification, with expected pixels typed by hand.
/// Neither the decoder nor the test builder produced these, so they check both independently.
/// </summary>
public class GoldenTests
{
    private const uint Red = 0xFFFF0000;
    private const uint Green = 0xFF00FF00;
    private const uint Blue = 0xFF0000FF;
    private const uint White = 0xFFFFFFFF;

    // Stored 24-bit pixels are B, G, R.
    private static readonly byte[] R = [0x00, 0x00, 0xFF];
    private static readonly byte[] G = [0x00, 0xFF, 0x00];
    private static readonly byte[] B = [0xFF, 0x00, 0x00];
    private static readonly byte[] W = [0xFF, 0xFF, 0xFF];

    /// <summary>The 2 × 2 image "red green / blue white" stored with each of the four origins.</summary>
    private static byte[] OrientationFile(byte descriptor) => Concat(
        Header(width: 2, height: 2, depth: 24, descriptor: descriptor),
        descriptor switch
        {
            0x00 => Concat(B, W, R, G), // bottom-left: bottom row first, left to right
            0x20 => Concat(R, G, B, W), // top-left
            0x10 => Concat(W, B, G, R), // bottom-right: bottom row first, right to left
            0x30 => Concat(G, R, W, B), // top-right
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor)),
        });

    [Theory]
    [InlineData((byte)0x00)]
    [InlineData((byte)0x20)]
    [InlineData((byte)0x10)]
    [InlineData((byte)0x30)]
    public void Orientation_AllFourOrigins(byte descriptor)
    {
        var image = Decode(OrientationFile(descriptor));
        Assert.Equal(new[] { Red, Green, Blue, White }, Pixels(image));
        Assert.Empty(image.Warnings);
    }

    [Theory]
    [InlineData((byte)0x00)]
    [InlineData((byte)0x20)]
    [InlineData((byte)0x10)]
    [InlineData((byte)0x30)]
    public void Builder_WritesTheSameBytesAsTheHandWrittenFile(byte descriptor)
    {
        var spec = new TgaSpec
        {
            Width = 2,
            Height = 2,
            PixelDepth = 24,
            RightToLeft = (descriptor & 0x10) != 0,
            TopToBottom = (descriptor & 0x20) != 0,
            Pixels = [R, G, B, W],
        };
        Assert.Equal(OrientationFile(descriptor), TgaBuilder.Bytes(spec));
    }

    [Fact]
    public void TrueColor32_KeepsBgraExactly()
    {
        var image = Decode(Concat(Header(width: 2, depth: 32, descriptor: 0x28), [0x1E, 0x14, 0x0A, 0x28, 0x32, 0x64, 0xC8, 0xFF]));
        Assert.Equal(new uint[] { 0x280A141E, 0xFFC86432 }, Pixels(image));
        Assert.Equal(TgaAlphaStorage.EightBit, image.Alpha.Storage);
        Assert.Equal(8, image.Alpha.DeclaredAttributeBits);
    }

    [Fact]
    public void TrueColor16_UsesTheAttributeBitAndExpandsFiveBitChannels()
    {
        // 0x7C00 = red 31, attribute 0; 0x83E0 = green 31, attribute 1; 0x0421 = 1,1,1 → 8,8,8.
        var image = Decode(Concat(Header(width: 3, depth: 16, descriptor: 0x21), [0x00, 0x7C, 0xE0, 0x83, 0x21, 0x04]));
        Assert.Equal(new uint[] { 0x00FF0000, 0xFF00FF00, 0x00080808 }, Pixels(image));
        Assert.Equal(TgaAlphaStorage.OneBit, image.Alpha.Storage);
    }

    [Fact]
    public void TrueColor15_IgnoresBit15()
    {
        var image = Decode(Concat(Header(width: 1, depth: 15, descriptor: 0x20), [0x1F, 0x80]));
        Assert.Equal(new[] { Blue }, Pixels(image));
        Assert.Equal(TgaAlphaStorage.None, image.Alpha.Storage);
    }

    [Fact]
    public void Greyscale8()
    {
        var image = Decode(Concat(Header(imageType: 3, width: 3, depth: 8, descriptor: 0x20), [0x00, 0x80, 0xFF]));
        Assert.Equal(new uint[] { 0xFF000000, 0xFF808080, 0xFFFFFFFF }, Pixels(image));
    }

    [Fact]
    public void Greyscale16_IsGreyThenAlpha()
    {
        var image = Decode(Concat(Header(imageType: 3, width: 2, depth: 16, descriptor: 0x28), [0x40, 0x80, 0xFF, 0x00]));
        Assert.Equal(new uint[] { 0x80404040, 0x00FFFFFF }, Pixels(image));
        Assert.Equal(TgaAlphaStorage.EightBit, image.Alpha.Storage);
    }

    [Fact]
    public void ColorMapped8_HonoursFirstIndex_AndMarksInvalidIndices()
    {
        var file = Concat(
            Header(colorMapType: 1, imageType: 1, firstIndex: 2, mapLength: 3, entryBits: 24, width: 4, depth: 8, descriptor: 0x20),
            R, G, B, // entries 2, 3, 4
            [0x02, 0x03, 0x04, 0x05]);
        var image = Decode(file);

        Assert.Equal(new uint[] { Red, Green, Blue, 0 }, Pixels(image));
        Assert.True(image.IsInvalidIndex(3, 0));
        Assert.False(image.IsInvalidIndex(2, 0));
        Assert.True(image.TryGetPaletteIndex(3, 0, out int index));
        Assert.Equal(5, index);
        Assert.True(HasWarning(image, TgaWarningCode.InvalidPaletteIndices));
    }

    [Fact]
    public void ColorMapped16_IndicesAbove255_With16BitEntries()
    {
        // Entries 300 = 0x7FFF (white, attribute 0) and 301 = 0x801F (blue, attribute 1).
        var file = Concat(
            Header(colorMapType: 1, imageType: 1, firstIndex: 300, mapLength: 2, entryBits: 16, width: 2, depth: 16, descriptor: 0x21),
            [0xFF, 0x7F, 0x1F, 0x80],
            [0x2D, 0x01, 0x2C, 0x01]);
        var image = Decode(file);

        Assert.Equal(new uint[] { 0xFF0000FF, 0x00FFFFFF }, Pixels(image));
        Assert.Equal(TgaAlphaStorage.OneBit, image.Alpha.Storage);
        Assert.True(image.TryGetPaletteIndex(0, 0, out int index));
        Assert.Equal(301, index);
    }

    [Fact]
    public void ColorMapped_32BitEntries()
    {
        var file = Concat(Header(colorMapType: 1, imageType: 1, mapLength: 1, entryBits: 32, depth: 8, descriptor: 0x28), [0x01, 0x02, 0x03, 0x04], [0x00]);
        Assert.Equal(new uint[] { 0x04030201 }, Pixels(Decode(file)));
    }

    [Fact]
    public void Rle_RunPacketContinuesIntoTheNextRow()
    {
        // 3 × 2: run of 4 red, then raw green, blue.
        var file = Concat(Header(imageType: 10, width: 3, height: 2, depth: 24, descriptor: 0x20), [0x83], R, [0x01], G, B);
        var image = Decode(file);
        Assert.Equal(new[] { Red, Red, Red, Red, Green, Blue }, Pixels(image));
        Assert.True(HasWarning(image, TgaWarningCode.RlePacketsCrossScanLines));
    }

    [Fact]
    public void Rle_PacketLongerThanTheImage_IsClipped()
    {
        var image = Decode(Concat(Header(imageType: 10, width: 2, depth: 24, descriptor: 0x20), [0x83], W));
        Assert.Equal(new[] { White, White }, Pixels(image));
        Assert.True(HasWarning(image, TgaWarningCode.RleOverrun));
    }

    [Fact]
    public void Rle_Greyscale()
    {
        var image = Decode(Concat(Header(imageType: 11, width: 4, depth: 8, descriptor: 0x20), [0x00, 10, 0x82, 200]));
        Assert.Equal(new uint[] { 0xFF0A0A0A, 0xFFC8C8C8, 0xFFC8C8C8, 0xFFC8C8C8 }, Pixels(image));
    }

    [Fact]
    public void Rle_ColorMapped_With15BitEntries()
    {
        // Entry 0 = 0x001F (blue), entry 1 = 0x7C00 (red); run of 2 × index 1, raw index 0.
        var file = Concat(
            Header(colorMapType: 1, imageType: 9, mapLength: 2, entryBits: 15, width: 3, depth: 8, descriptor: 0x20),
            [0x1F, 0x00, 0x00, 0x7C],
            [0x81, 0x01, 0x00, 0x00]);
        Assert.Equal(new[] { Red, Red, Blue }, Pixels(Decode(file)));
    }

    [Fact]
    public void Interleave_TwoWay_TopToBottom()
    {
        // Stored rows fill image rows 0, 2, 4, then 1, 3.
        var image = Decode(Concat(Header(imageType: 3, width: 1, height: 5, depth: 8, descriptor: 0x60), [10, 20, 30, 40, 50]));
        Assert.Equal(new[] { 10, 40, 20, 50, 30 }, Pixels(image).Select(p => (int)(p & 0xFF)));
    }

    [Fact]
    public void Interleave_FourWay_BottomToTop()
    {
        // Logical rows 0, 4, 1, 5, 2, 6, 3, then flipped for the bottom-left origin: 6, 2, 5, 1, 4, 0, 3.
        var image = Decode(Concat(Header(imageType: 3, width: 1, height: 7, depth: 8, descriptor: 0x80), [1, 2, 3, 4, 5, 6, 7]));
        Assert.Equal(new[] { 6, 4, 2, 7, 5, 3, 1 }, Pixels(image).Select(p => (int)(p & 0xFF)));
    }

    [Fact]
    public void Interleave_TwoWay_WithRle()
    {
        // Stored rows 10, 10, 20, 20 go to image rows 0, 2, 1, 3.
        var image = Decode(Concat(Header(imageType: 11, width: 1, height: 4, depth: 8, descriptor: 0x60), [0x81, 10, 0x81, 20]));
        Assert.Equal(new[] { 10, 20, 10, 20 }, Pixels(image).Select(p => (int)(p & 0xFF)));
    }

    [Fact]
    public void Builder_InterleavesLikeTheHandWrittenFile()
    {
        var spec = new TgaSpec { ImageType = 3, Width = 1, Height = 5, PixelDepth = 8, TopToBottom = true, Interleave = 1, Pixels = [[10], [40], [20], [50], [30]] };
        Assert.Equal(Concat(Header(imageType: 3, width: 1, height: 5, depth: 8, descriptor: 0x60), [10, 20, 30, 40, 50]), TgaBuilder.Bytes(spec));
    }

    [Fact]
    public void ImageId_IsSkippedAndKept()
    {
        var image = Decode(Concat(Header(idLength: 5, descriptor: 0x20), "hello"u8.ToArray(), R));
        Assert.Equal(new[] { Red }, Pixels(image));
        Assert.Equal("\"hello\"", image.Metadata.ImageIdText);
    }

    [Fact]
    public void UnusedColorMap_InATrueColourFile_IsSkipped()
    {
        var image = Decode(Concat(Header(colorMapType: 1, imageType: 2, mapLength: 2, entryBits: 24, descriptor: 0x20), [1, 2, 3, 4, 5, 6], G));
        Assert.Equal(new[] { Green }, Pixels(image));
        Assert.True(image.Metadata.HasUnusedColorMap);
        Assert.True(HasWarning(image, TgaWarningCode.UnusedColorMap));
    }

    [Fact]
    public void DataAfterThePixels_IsIgnored()
    {
        var image = Decode(Concat(Header(descriptor: 0x20), R, "extra data"u8.ToArray()));
        Assert.Equal(new[] { Red }, Pixels(image));
        Assert.False(image.Metadata.HasFooter);
        Assert.Equal(21, image.Metadata.PixelDataEnd);
    }

    [Fact]
    public void Footer_WithNoStructures_MarksTga2()
    {
        var footer = Concat([0, 0, 0, 0, 0, 0, 0, 0], "TRUEVISION-XFILE.\0"u8.ToArray());
        var image = Decode(Concat(Header(descriptor: 0x20), R, footer));
        Assert.Equal(new[] { Red }, Pixels(image));
        Assert.True(image.Metadata.HasFooter);
        Assert.Equal(TgaStructureStatus.Absent, image.Metadata.Extension.Status);
    }
}
