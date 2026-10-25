using System.Security.Cryptography;
using System.Text.Json;

namespace TgaViewer.Core.Tests;

/// <summary>
/// Compares the decoder with an independent one, Pillow, on the files in Reference/inputs (seeded random
/// pixels from TgaReferenceSamples). Pillow's outputs are committed, so these tests need no Python; see
/// tools/reference/README.md for how they were made. Each file's agreement level comes from how Pillow
/// 13.0.0 reads that variant (its TgaImagePlugin.py and pixel unpackers), never from whether the outputs
/// happen to match.
/// </summary>
public class ReferenceTests
{
    private const string PillowVersion = "Pillow 13.0.0";

    private const string FiveBit = "Pillow expands 5-bit values as v*255/31, truncated (the viewer replicates bits), " +
        "and reads the attribute bit as inverted alpha (rawmode BGRA;15Z), so only the stored 5-bit colour values are compared.";

    private enum Agreement
    {
        /// <summary>Every channel of every pixel must be identical.</summary>
        Exact,

        /// <summary>Red, green and blue must be identical; alpha is not compared.</summary>
        Colour,

        /// <summary>The stored 5-bit red, green and blue values must be identical; alpha is not compared.</summary>
        FiveBitColour,

        /// <summary>Not compared: Pillow cannot read the file, or reads it differently by design.</summary>
        Excluded,
    }

    private static readonly Dictionary<string, (Agreement Level, string Reason)> Cases = new()
    {
        ["tc24-bottom-left.tga"] = (Agreement.Exact, ""),
        ["tc24-top-left.tga"] = (Agreement.Exact, ""),
        ["tc24-bottom-right.tga"] = (Agreement.Exact, ""),
        ["tc24-top-right.tga"] = (Agreement.Exact, ""),
        ["tc24-image-id.tga"] = (Agreement.Exact, ""),
        ["tc32-alpha-bits-8.tga"] = (Agreement.Exact, ""),
        ["tc32-alpha-bits-0-top-left.tga"] = (Agreement.Exact, ""),
        ["grey8-bottom-left.tga"] = (Agreement.Exact, ""),
        ["grey8-top-right.tga"] = (Agreement.Exact, ""),
        ["grey16-with-alpha.tga"] = (Agreement.Exact, ""),
        ["cm8-palette24.tga"] = (Agreement.Exact, ""),
        ["cm8-palette24-from-index-16.tga"] = (Agreement.Exact, ""),
        ["cm8-palette32.tga"] = (Agreement.Exact, ""),
        ["rle-tc24-within-rows.tga"] = (Agreement.Exact, ""),
        ["rle-tc24-wide-rows.tga"] = (Agreement.Exact, ""),
        ["rle-tc24-across-rows-top-right.tga"] = (Agreement.Exact, ""),
        ["rle-tc32-across-rows.tga"] = (Agreement.Exact, ""),
        ["rle-grey8-across-rows.tga"] = (Agreement.Exact, ""),
        ["rle-grey16-with-alpha-within-rows.tga"] = (Agreement.Exact, ""),
        ["rle-cm8-palette24-within-rows.tga"] = (Agreement.Exact, ""),
        ["tga2-attributes-3-all-structures.tga"] = (Agreement.Exact, ""),
        ["tga2-attributes-4-premultiplied.tga"] = (Agreement.Exact, ""),
        ["tga2-rle-attributes-3.tga"] = (Agreement.Exact, ""),
        ["tga2-attributes-0.tga"] = (Agreement.Colour,
            "Pillow sets alpha to 255 when the extension area declares no alpha (attributes type 0); the viewer keeps the stored values and decides only when displaying."),
        ["tc16-attribute-bits-1.tga"] = (Agreement.FiveBitColour, FiveBit),
        ["tc16-attribute-bits-0-top-left.tga"] = (Agreement.FiveBitColour, FiveBit),
        ["cm8-palette16.tga"] = (Agreement.FiveBitColour, FiveBit),
        ["rle-tc16-across-rows.tga"] = (Agreement.FiveBitColour, FiveBit),
        ["tc15.tga"] = (Agreement.Excluded, "Pillow does not read 15-bit images."),
        ["cm8-palette15.tga"] = (Agreement.Excluded, "Pillow does not read 15-bit palette entries."),
        ["cm16-palette24-from-index-300.tga"] = (Agreement.Excluded, "Pillow does not read 16-bit palette indices."),
        ["tc24-unused-colour-map.tga"] = (Agreement.Excluded, "Pillow fails on true-colour files that carry a colour map (\"unrecognized image mode\")."),
        ["rle-tc24-across-rows-top-left.tga"] = (Agreement.Excluded, "Pillow rejects run packets that cross rows (\"buffer overrun\"); it accepts raw packets that do."),
        ["interleaved-two-way.tga"] = (Agreement.Excluded, "Pillow ignores the interleaving bits and reads the rows in plain order."),
        ["interleaved-four-way.tga"] = (Agreement.Excluded, "Pillow ignores the interleaving bits and reads the rows in plain order."),
    };

    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Reference");

    public static TheoryData<string> ComparedFiles()
    {
        var data = new TheoryData<string>();
        foreach (var (file, (level, _)) in Cases)
        {
            if (level != Agreement.Excluded)
            {
                data.Add(file);
            }
        }

        return data;
    }

    [Fact]
    public void EveryInputHasAnAgreementLevelAndItsOwnPillowResult()
    {
        PillowManifest manifest = Manifest();
        Assert.True(manifest.Decoder == PillowVersion, $"The agreement levels were set for {PillowVersion}; review them before accepting outputs from {manifest.Decoder}.");

        string[] inputs = Directory.GetFiles(Path.Combine(Folder, "inputs"), "*.tga").Select(Path.GetFileName).OfType<string>().Order().ToArray();
        Assert.Equal(Cases.Keys.Order(), inputs);
        Assert.Equal(inputs, manifest.Cases.Select(c => c.File).Order());
        foreach (PillowCase entry in manifest.Cases)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(Folder, "inputs", entry.File));
            Assert.True(Convert.ToHexStringLower(SHA256.HashData(bytes)) == entry.Sha256, $"{entry.File} has changed since Pillow decoded it; regenerate the references.");
        }
    }

    [Fact]
    public void EveryPartialOrExcludedComparisonSaysWhy()
    {
        Assert.All(Cases.Where(c => c.Value.Level != Agreement.Exact), c => Assert.False(string.IsNullOrWhiteSpace(c.Value.Reason), c.Key));
    }

    [Theory]
    [MemberData(nameof(ComparedFiles))]
    public void DecodedPixelsAgreeWithPillow(string file)
    {
        Agreement level = Cases[file].Level;
        PillowCase pillow = Manifest().Cases.Single(c => c.File == file);
        Assert.True(pillow.Status == "decoded", $"{file}: Pillow {pillow.Status} it ({pillow.Error}), but its agreement level is {level}.");

        TgaImage image = Helpers.Decode(File.ReadAllBytes(Path.Combine(Folder, "inputs", file)));
        Assert.Equal((pillow.Width, pillow.Height), (image.Width, image.Height));
        byte[] rgba = File.ReadAllBytes(Path.Combine(Folder, pillow.Pixels!));
        Assert.Equal(image.PixelCount * 4, rgba.Length);

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                uint p = image.GetPixel(x, y);
                var ours = ((int)(p >> 16) & 255, (int)(p >> 8) & 255, (int)p & 255, (int)(p >> 24));
                int i = (y * image.Width + x) * 4;
                var theirs = (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
                bool same = level switch
                {
                    Agreement.Exact => ours == theirs,
                    Agreement.Colour => (ours.Item1, ours.Item2, ours.Item3) == (theirs.Item1, theirs.Item2, theirs.Item3),
                    _ => (ours.Item1 >> 3, ours.Item2 >> 3, ours.Item3 >> 3) == (FiveBits(theirs.Item1), FiveBits(theirs.Item2), FiveBits(theirs.Item3)),
                };
                if (!same)
                {
                    Assert.Fail($"{file} pixel ({x}, {y}): viewer RGBA {ours}, Pillow RGBA {theirs} (compared: {level}).");
                }
            }
        }
    }

    /// <summary>The 5-bit value behind an 8-bit expansion: exact for truncated, rounded or bit-replicated expansions.</summary>
    private static int FiveBits(int value) => (value * 31 + 127) / 255;

    private static PillowManifest Manifest() =>
        JsonSerializer.Deserialize<PillowManifest>(
            File.ReadAllText(Path.Combine(Folder, "pillow-manifest.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private sealed record PillowManifest(string Decoder, string Python, PillowCase[] Cases);

    private sealed record PillowCase(string File, string Sha256, string Status, int Width, int Height, string? Pixels, string? Error);
}
