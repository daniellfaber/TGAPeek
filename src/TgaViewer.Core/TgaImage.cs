using System.Numerics;

namespace TgaViewer.Core;

/// <summary>What the stored pixel (or palette entry) format physically holds in its alpha position.</summary>
public enum TgaAlphaStorage
{
    None,
    OneBit,
    EightBit,
}

/// <summary>Facts about alpha in a decoded file. How alpha is shown is decided separately by <see cref="AlphaPolicy"/>.</summary>
public sealed record TgaAlphaInfo
{
    public TgaAlphaStorage Storage { get; init; }

    /// <summary>Attribute bits declared in the header (field 5.6 bits 3–0).</summary>
    public int DeclaredAttributeBits { get; init; }

    /// <summary>TGA 2.0 extension-area attributes type (field 24, values 0–4), when a valid extension area defines one.</summary>
    public byte? AttributesType { get; init; }

    /// <summary>Every decoded stored alpha value is 0.</summary>
    public bool AllZero { get; init; }

    /// <summary>Every decoded alpha value is 255 (always true when no alpha is stored).</summary>
    public bool AllOpaque { get; init; }

    public bool HasStoredAlpha => Storage != TgaAlphaStorage.None;
}

/// <summary>A decoded colour map. Valid indices run from <see cref="FirstIndex"/> to <see cref="LastIndex"/>.</summary>
public sealed class TgaPalette
{
    private readonly uint[] _entries;

    internal TgaPalette(int firstIndex, int entryBits, uint[] entries)
    {
        FirstIndex = firstIndex;
        EntryBits = entryBits;
        _entries = entries;
    }

    public int FirstIndex { get; }

    public int EntryBits { get; }

    public int Length => _entries.Length;

    public int LastIndex => FirstIndex + _entries.Length - 1;

    /// <summary>Entries as 0xAARRGGBB, in index order starting at <see cref="FirstIndex"/>.</summary>
    public ReadOnlySpan<uint> Entries => _entries;

    public bool Contains(int index) => (uint)(index - FirstIndex) < (uint)_entries.Length;

    public uint this[int index] => _entries[index - FirstIndex];
}

/// <summary>One bit per pixel, indexed by y × width + x.</summary>
public sealed class TgaBitMask
{
    private readonly ulong[] _bits;

    internal TgaBitMask(long length)
    {
        Length = length;
        _bits = new ulong[(length + 63) >> 6];
    }

    public long Length { get; }

    public bool this[long index] => (_bits[index >> 6] & (1UL << (int)(index & 63))) != 0;

    public long CountSet()
    {
        long count = 0;
        foreach (ulong word in _bits)
        {
            count += BitOperations.PopCount(word);
        }

        return count;
    }

    internal void Set(long index) => _bits[index >> 6] |= 1UL << (int)(index & 63);

    internal static long ByteSize(long length) => ((length + 63) >> 6) * sizeof(ulong);
}

/// <summary>
/// A decoded TGA image. Pixels are stored exactly as decoded: rows top to bottom, left to right,
/// each value 0xAARRGGBB (the byte order Windows uses for 32-bit bitmaps). Nothing here is
/// premultiplied, gamma-corrected or otherwise changed for display.
/// </summary>
public sealed class TgaImage
{
    private readonly uint[][] _rows;
    private readonly byte[][]? _indices8;
    private readonly ushort[][]? _indices16;
    private readonly int[] _storedRowOfImageRow;
    private long _invalidPremultiplied = -1;

    internal TgaImage(
        TgaHeader header,
        uint[][] rows,
        byte[][]? indices8,
        ushort[][]? indices16,
        int[] storedRowOfImageRow,
        TgaPalette? palette,
        TgaBitMask? missingPixels,
        TgaBitMask? invalidIndexPixels,
        long decodedPixelCount,
        TgaAlphaInfo alpha,
        TgaMetadata metadata,
        IReadOnlyList<TgaWarning> warnings,
        long memoryBytes)
    {
        Header = header;
        _rows = rows;
        _indices8 = indices8;
        _indices16 = indices16;
        _storedRowOfImageRow = storedRowOfImageRow;
        Palette = palette;
        MissingPixels = missingPixels;
        InvalidIndexPixels = invalidIndexPixels;
        DecodedPixelCount = decodedPixelCount;
        Alpha = alpha;
        Metadata = metadata;
        Warnings = warnings;
        MemoryBytes = memoryBytes;
        FormatDescription = Describe(header);
    }

    public TgaHeader Header { get; }

    public int Width => Header.Width;

    public int Height => Header.Height;

    public long PixelCount => Header.PixelCount;

    public TgaPalette? Palette { get; }

    /// <summary>Pixels the file ended before reaching (null when the image is complete).</summary>
    public TgaBitMask? MissingPixels { get; }

    /// <summary>Pixels whose palette index lies outside the colour map (null when there are none).</summary>
    public TgaBitMask? InvalidIndexPixels { get; }

    public bool IsPartial => MissingPixels is not null;

    public long DecodedPixelCount { get; }

    public TgaAlphaInfo Alpha { get; }

    public TgaMetadata Metadata { get; }

    public IReadOnlyList<TgaWarning> Warnings { get; }

    /// <summary>Approximate bytes held by this image's pixel, index and palette buffers.</summary>
    public long MemoryBytes { get; }

    /// <summary>For example "32-bit true-colour (BGRA), RLE".</summary>
    public string FormatDescription { get; }

    public ReadOnlySpan<uint> GetRow(int y) => _rows[y];

    public uint GetPixel(int x, int y) => _rows[y][x];

    internal uint[] RowArray(int y) => _rows[y];

    public bool TryGetPaletteIndex(int x, int y, out int index)
    {
        if (_indices8 is not null)
        {
            index = _indices8[y][x];
            return true;
        }

        if (_indices16 is not null)
        {
            index = _indices16[y][x];
            return true;
        }

        index = -1;
        return false;
    }

    public bool IsMissing(int x, int y) => MissingPixels is { } mask && mask[(long)y * Width + x];

    public bool IsInvalidIndex(int x, int y) => InvalidIndexPixels is { } mask && mask[(long)y * Width + x];

    /// <summary>Where pixel (x, y) is stored: its row and column in file order.</summary>
    public (int Row, int Column) GetStoredPosition(int x, int y) =>
        (_storedRowOfImageRow[y], Header.RightToLeft ? Width - 1 - x : x);

    /// <summary>Pixels whose colour exceeds their alpha, which is invalid for premultiplied data (computed once).</summary>
    public long CountInvalidPremultipliedPixels()
    {
        if (_invalidPremultiplied >= 0)
        {
            return _invalidPremultiplied;
        }

        long count = 0;
        foreach (uint[] row in _rows)
        {
            foreach (uint pixel in row)
            {
                if (!DisplayRenderer.IsValidPremultiplied(pixel))
                {
                    count++;
                }
            }
        }

        _invalidPremultiplied = count;
        return count;
    }

    internal static string Describe(TgaHeader header)
    {
        string kind = header.Kind switch
        {
            TgaImageKind.TrueColor => header.PixelDepth switch
            {
                15 => "15-bit true-colour (5-5-5)",
                16 => "16-bit true-colour (5-5-5 + attribute bit)",
                24 => "24-bit true-colour (BGR)",
                _ => "32-bit true-colour (BGRA)",
            },
            TgaImageKind.Grayscale => header.PixelDepth == 8 ? "8-bit greyscale" : "16-bit greyscale + alpha",
            _ => $"{header.PixelDepth}-bit colour-mapped ({header.ColorMapLength} × {header.ColorMapEntryBits}-bit entries)",
        };
        return $"{kind}, {(header.IsRunLengthEncoded ? "RLE" : "uncompressed")}";
    }
}
