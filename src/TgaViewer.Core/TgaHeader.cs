using System.Buffers.Binary;

namespace TgaViewer.Core;

public enum TgaImageKind
{
    ColorMapped,
    TrueColor,
    Grayscale,
}

/// <summary>Descriptor bits 7–6 (TGA 1.0 interleaving; TGA 2.0 requires zero).</summary>
public enum TgaInterleave
{
    None = 0,
    TwoWay = 1,
    FourWay = 2,
    Reserved = 3,
}

/// <summary>The 18-byte TGA file header (TGA 2.0 spec, fields 1–5), exactly as stored.</summary>
public sealed record TgaHeader
{
    public const int Size = 18;

    public byte IdLength { get; init; }
    public byte ColorMapType { get; init; }
    public byte ImageType { get; init; }
    public ushort ColorMapFirstIndex { get; init; }
    public ushort ColorMapLength { get; init; }
    public byte ColorMapEntryBits { get; init; }
    public ushort XOrigin { get; init; }
    public ushort YOrigin { get; init; }
    public ushort Width { get; init; }
    public ushort Height { get; init; }
    public byte PixelDepth { get; init; }
    public byte Descriptor { get; init; }

    /// <summary>Field 5.6 bits 3–0: attribute (alpha) bits per pixel.</summary>
    public int AttributeBits => Descriptor & 0x0F;

    /// <summary>Field 5.6 bit 4: pixels are stored right to left.</summary>
    public bool RightToLeft => (Descriptor & 0x10) != 0;

    /// <summary>Field 5.6 bit 5: rows are stored top to bottom (otherwise bottom to top).</summary>
    public bool TopToBottom => (Descriptor & 0x20) != 0;

    public TgaInterleave Interleave => (TgaInterleave)((Descriptor >> 6) & 3);

    public bool IsRunLengthEncoded => ImageType is 9 or 10 or 11;

    public TgaImageKind? Kind => ImageType switch
    {
        1 or 9 => TgaImageKind.ColorMapped,
        2 or 10 => TgaImageKind.TrueColor,
        3 or 11 => TgaImageKind.Grayscale,
        _ => null,
    };

    public int BytesPerPixel => (PixelDepth + 7) / 8;

    /// <summary>Each colour-map entry occupies a whole number of bytes (field 4.3).</summary>
    public int ColorMapEntryBytes => (ColorMapEntryBits + 7) / 8;

    public long PixelCount => (long)Width * Height;

    /// <summary>Bytes the colour map occupies in the file (only when ColorMapType is 1).</summary>
    public long ColorMapByteLength => ColorMapType == 1 ? (long)ColorMapLength * ColorMapEntryBytes : 0;

    public string ImageTypeName => ImageType switch
    {
        0 => "No image data",
        1 or 9 => "Colour-mapped",
        2 or 10 => "True-colour",
        3 or 11 => "Greyscale",
        32 or 33 => "Huffman/delta compressed colour-mapped",
        _ => $"Unknown ({ImageType})",
    };

    public string CompressionName => IsRunLengthEncoded ? "RLE" : ImageType is 32 or 33 ? "Huffman/delta" : "Uncompressed";

    /// <summary>Where the first stored pixel goes, for example "bottom-left" (the TGA default).</summary>
    public string OriginName => (TopToBottom ? "top" : "bottom") + "-" + (RightToLeft ? "right" : "left");

    public static TgaHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
        {
            throw new ArgumentException("A TGA header is 18 bytes long.", nameof(data));
        }

        return new TgaHeader
        {
            IdLength = data[0],
            ColorMapType = data[1],
            ImageType = data[2],
            ColorMapFirstIndex = BinaryPrimitives.ReadUInt16LittleEndian(data[3..]),
            ColorMapLength = BinaryPrimitives.ReadUInt16LittleEndian(data[5..]),
            ColorMapEntryBits = data[7],
            XOrigin = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]),
            YOrigin = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]),
            Width = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]),
            Height = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]),
            PixelDepth = data[16],
            Descriptor = data[17],
        };
    }
}
