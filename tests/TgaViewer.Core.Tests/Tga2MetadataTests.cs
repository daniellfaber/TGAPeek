using System.Buffers.Binary;
using TgaViewer.TestSupport;
using static TgaViewer.Core.Tests.Helpers;

namespace TgaViewer.Core.Tests;

public class Tga2MetadataTests
{
    private static readonly byte[][] StampPixels =
    [
        TgaPixels.Bgra(255, 0, 0, 255), TgaPixels.Bgra(0, 255, 0, 255), TgaPixels.Bgra(0, 0, 255, 255),
        TgaPixels.Bgra(10, 20, 30, 40), TgaPixels.Bgra(50, 60, 70, 80), TgaPixels.Bgra(90, 100, 110, 120),
    ];

    private static TgaSpec FullSpec(int origin = 2, byte attributesType = 3, bool rle = false)
    {
        var spec = TgaSamples.TrueColor(32, origin: origin, attributeBits: 8, rle: rle, width: 6, height: 4);
        spec.Tga2 = new Tga2Spec
        {
            AuthorName = "Ada Lovelace",
            AuthorComments = ["first line", "second line"],
            Timestamp = [10, 25, 2026, 12, 34, 56],
            JobName = "Job 7",
            JobTime = [1, 2, 3],
            SoftwareId = "Tests",
            SoftwareVersionNumber = 123,
            SoftwareVersionLetter = (byte)'b',
            KeyColor = 0x80FF0000,
            AspectNumerator = 4,
            AspectDenominator = 3,
            GammaNumerator = 22,
            GammaDenominator = 10,
            AttributesType = attributesType,
            ColorCorrectionTable = Enumerable.Range(0, 1024).Select(i => (ushort)(i * 61)).ToArray(),
            StampWidth = 3,
            StampHeight = 2,
            StampPixels = StampPixels,
            IncludeScanLineTable = true,
            DeveloperFields = [(100, "hello"u8.ToArray()), (200, [1, 2, 3])],
        };
        return spec;
    }

    private static uint[] CleanPixels(TgaSpec spec)
    {
        var tga2 = spec.Tga2;
        spec.Tga2 = null;
        uint[] pixels = Pixels(Decode(spec));
        spec.Tga2 = tga2;
        return pixels;
    }

    [Fact]
    public void ReadsEveryStructure()
    {
        var spec = FullSpec();
        var build = TgaBuilder.Build(spec);
        var image = Decode(build.Bytes);
        var m = image.Metadata;

        Assert.True(m.HasFooter);
        Assert.Empty(image.Warnings);
        Assert.Equal(CleanPixels(spec), Pixels(image));

        var e = Assert.IsType<TgaExtensionArea>(m.Extension.Value);
        Assert.Equal(build.ExtensionOffset, m.Extension.Offset);
        Assert.Equal("Ada Lovelace", e.AuthorName);
        Assert.Equal(new[] { "first line", "second line", "", "" }, e.AuthorComments);
        Assert.Equal("2026-10-25 12:34:56", e.Timestamp);
        Assert.Equal("Job 7", e.JobName);
        Assert.Equal("1:02:03", e.JobTime);
        Assert.Equal("Tests", e.SoftwareId);
        Assert.Equal("1.23b", e.SoftwareVersion);
        Assert.Equal(0x80FF0000u, e.KeyColor);
        Assert.Equal(4.0 / 3.0, e.PixelAspectRatio);
        Assert.Equal(2.2, e.Gamma!.Value, 6);
        Assert.Equal((byte?)3, image.Alpha.AttributesType);

        var developer = Assert.IsType<TgaDeveloperDirectory>(m.DeveloperDirectory.Value);
        Assert.Equal(new ushort[] { 100, 200 }, developer.Entries.Select(entry => entry.Tag));
        Assert.All(developer.Entries, entry => Assert.True(entry.IsValid));
        Assert.Equal("hello"u8.ToArray(), developer.Previews[0]);
        Assert.Equal(new byte[] { 1, 2, 3 }, developer.Previews[1]);

        var table = Assert.IsType<TgaColorCorrectionTable>(m.ColorCorrection.Value);
        Assert.Equal(spec.Tga2!.ColorCorrectionTable, table.Values);

        var stamp = Assert.IsType<TgaPostageStamp>(m.PostageStamp.Value);
        Assert.Equal(3, stamp.Width);
        Assert.Equal(2, stamp.Height);
        Assert.Equal(StampPixels.Select(p => TgaPixels.Direct(32, p)), stamp.Pixels);

        var scan = Assert.IsType<TgaScanLineTable>(m.ScanLineTable.Value);
        Assert.Equal(0, scan.MismatchCount);
        Assert.Equal(build.PixelDataOffset, (int)scan.Offsets[0]);
        Assert.Equal(build.PixelDataOffset + 6 * 4, (int)scan.Offsets[1]);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    [InlineData((byte)2)]
    [InlineData((byte)3)]
    [InlineData((byte)4)]
    public void AttributesTypeIsReported(byte type)
    {
        var image = Decode(FullSpec(attributesType: type));
        Assert.Equal(type, image.Alpha.AttributesType);
    }

    [Fact]
    public void UndefinedAttributesType_FallsBackToTheHeader()
    {
        var image = Decode(FullSpec(attributesType: 9));
        Assert.Null(image.Alpha.AttributesType);
        Assert.True(HasWarning(image, TgaWarningCode.UnknownAttributesType));
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    [InlineData((byte)2)]
    [InlineData((byte)3)]
    public void PostageStampFollowsTheImageOrigin(int origin)
    {
        var stamp = Decode(FullSpec(origin)).Metadata.PostageStamp.Value!;
        Assert.Equal(StampPixels.Select(p => TgaPixels.Direct(32, p)), stamp.Pixels);
    }

    [Fact]
    public void RleScanLineTablePointsIntoThePixelData()
    {
        var image = Decode(FullSpec(rle: true));
        Assert.Equal(0, image.Metadata.ScanLineTable.Value!.MismatchCount);
        Assert.DoesNotContain(image.Warnings, w => !w.IsNote);
    }

    /// <summary>Each case changes the spec before writing, or the written bytes afterwards.</summary>
    public static IEnumerable<object?[]> CorruptionCases()
    {
        static object?[] Spec(string name, Action<Tga2Spec> change, TgaWarningCode expected) => [name, change, null, expected];
        static object?[] Bytes(string name, Action<byte[], TgaBuild> change, TgaWarningCode expected) => [name, null, change, expected];

        yield return Spec("extension offset past the end", t => t.ExtensionOffsetOverride = 1_000_000, TgaWarningCode.ExtensionAreaInvalid);
        yield return Spec("extension offset inside the pixels", t => t.ExtensionOffsetOverride = 30, TgaWarningCode.ExtensionAreaInvalid);
        yield return Spec("extension size too small", t => t.ExtensionSizeField = 100, TgaWarningCode.ExtensionAreaInvalid);
        yield return Spec("developer directory past the end", t => t.DeveloperDirectoryOffsetOverride = 0xFFFFFFFF, TgaWarningCode.DeveloperDirectoryInvalid);
        yield return Spec("colour-correction table past the end", t => t.ColorCorrectionOffsetOverride = 900_000, TgaWarningCode.ColorCorrectionTableInvalid);
        yield return Spec("postage stamp past the end", t => t.PostageStampOffsetOverride = 900_000, TgaWarningCode.PostageStampInvalid);
        yield return Spec("scan-line table past the end", t => t.ScanLineOffsetOverride = 900_000, TgaWarningCode.ScanLineTableInvalid);
        yield return Bytes("developer directory claims 60000 entries", (b, f) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(f.DeveloperDirectoryOffset), 60000), TgaWarningCode.DeveloperDirectoryInvalid);
        yield return Bytes("developer entry outside the file", (b, f) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(f.DeveloperDirectoryOffset + 4), 0x7FFFFFF0), TgaWarningCode.DeveloperEntriesInvalid);
        yield return Bytes("postage stamp claims 255 × 255", (b, f) => b[f.PostageStampOffset] = b[f.PostageStampOffset + 1] = 255, TgaWarningCode.PostageStampInvalid);
        yield return Bytes("scan-line entry wrong", (b, f) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(f.ScanLineOffset + 4), 3), TgaWarningCode.ScanLineTableMismatch);
    }

    [Theory]
    [MemberData(nameof(CorruptionCases))]
    public void CorruptStructuresAreIgnored_AndNeverChangeThePixels(string name, Action<Tga2Spec>? specChange, Action<byte[], TgaBuild>? byteChange, TgaWarningCode expected)
    {
        var spec = FullSpec();
        uint[] clean = CleanPixels(spec);
        specChange?.Invoke(spec.Tga2!);
        var build = TgaBuilder.Build(spec);
        byte[] bytes = build.Bytes;
        byteChange?.Invoke(bytes, build);

        var image = Decode(bytes);
        Assert.True(HasWarning(image, expected), $"{name}: expected warning {expected}; got {string.Join(", ", image.Warnings.Select(w => w.Code))}");
        Assert.Equal(clean, Pixels(image));
    }

    [Fact]
    public void LargerExtensionArea_IsReadWithANote()
    {
        var spec = FullSpec();
        spec.Tga2!.ExtensionSizeField = 600;
        var image = Decode(spec);
        Assert.Equal(TgaStructureStatus.Valid, image.Metadata.Extension.Status);
        Assert.True(HasWarning(image, TgaWarningCode.ExtensionAreaLarger));
    }

    [Fact]
    public void DamagedSignature_MeansNoFooter()
    {
        var spec = FullSpec();
        byte[] bytes = TgaBuilder.Bytes(spec);
        bytes[^5] ^= 0xFF;
        var image = Decode(bytes);
        Assert.False(image.Metadata.HasFooter);
        Assert.Equal(TgaStructureStatus.Absent, image.Metadata.Extension.Status);
        Assert.Equal(CleanPixels(spec), Pixels(image));
    }

    [Fact]
    public void RandomDamageAfterThePixels_NeverChangesThePixels()
    {
        foreach (bool rle in new[] { false, true })
        {
            var spec = FullSpec(origin: 1, rle: rle);
            uint[] clean = CleanPixels(spec);
            var build = TgaBuilder.Build(spec);
            var random = new Random(rle ? 2 : 1);
            for (int i = 0; i < 400; i++)
            {
                byte[] bytes = (byte[])build.Bytes.Clone();
                int changes = random.Next(1, 12);
                for (int c = 0; c < changes; c++)
                {
                    bytes[random.Next(build.PixelDataEnd, bytes.Length)] = (byte)random.Next(256);
                }

                Assert.Equal(clean, Pixels(Decode(bytes)));
            }
        }
    }
}
