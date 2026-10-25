using System.Buffers.Binary;

namespace TgaViewer.Core.Decoding;

/// <summary>How one stored pixel is laid out in the file.</summary>
internal enum PixelLayout
{
    Bgr24,
    Bgra32,
    Rgb555,
    Argb1555,
    Gray8,
    GrayAlpha16,
    Index8,
    Index16,
}

/// <summary>Converts stored pixels to 0xAARRGGBB without changing any stored channel value.</summary>
internal static class PixelConversion
{
    public static PixelLayout LayoutFor(TgaHeader header) => header.Kind switch
    {
        TgaImageKind.TrueColor => header.PixelDepth switch
        {
            15 => PixelLayout.Rgb555,
            16 => PixelLayout.Argb1555,
            24 => PixelLayout.Bgr24,
            _ => PixelLayout.Bgra32,
        },
        TgaImageKind.Grayscale => header.PixelDepth == 8 ? PixelLayout.Gray8 : PixelLayout.GrayAlpha16,
        _ => header.PixelDepth == 8 ? PixelLayout.Index8 : PixelLayout.Index16,
    };

    public static int BytesPerPixel(PixelLayout layout) => layout switch
    {
        PixelLayout.Bgr24 => 3,
        PixelLayout.Bgra32 => 4,
        PixelLayout.Gray8 or PixelLayout.Index8 => 1,
        _ => 2,
    };

    /// <summary>
    /// Expands a 5-bit channel to 8 bits by bit replication: 0 → 0, 31 → 255, and the stored
    /// value is always recoverable as v8 >> 3.
    /// </summary>
    public static uint Expand5(uint value) => (value << 3) | (value >> 2);

    /// <summary>A stored 5-5-5 word; bit 15 is the attribute bit when <paramref name="hasAttributeBit"/> (16-bit), otherwise unused (15-bit).</summary>
    public static uint FromWord(uint word, bool hasAttributeBit)
    {
        uint alpha = !hasAttributeBit || (word & 0x8000) != 0 ? 0xFFu : 0u;
        return alpha << 24
            | Expand5((word >> 10) & 0x1F) << 16
            | Expand5((word >> 5) & 0x1F) << 8
            | Expand5(word & 0x1F);
    }

    /// <summary>A stored colour-map entry (field 7; entry sizes 15, 16, 24 and 32 bits).</summary>
    public static uint FromPaletteEntry(ReadOnlySpan<byte> entry, int entryBits) => entryBits switch
    {
        15 => FromWord(BinaryPrimitives.ReadUInt16LittleEndian(entry), hasAttributeBit: false),
        16 => FromWord(BinaryPrimitives.ReadUInt16LittleEndian(entry), hasAttributeBit: true),
        24 => 0xFF000000u | (uint)entry[2] << 16 | (uint)entry[1] << 8 | entry[0],
        _ => BinaryPrimitives.ReadUInt32LittleEndian(entry),
    };

    /// <summary>Converts stored true-colour or greyscale pixels; <paramref name="destination"/>.Length pixels are read.</summary>
    public static void ConvertDirect(PixelLayout layout, ReadOnlySpan<byte> source, Span<uint> destination)
    {
        int count = destination.Length;
        switch (layout)
        {
            case PixelLayout.Bgr24:
                for (int i = 0, s = 0; i < count; i++, s += 3)
                {
                    destination[i] = 0xFF000000u | (uint)source[s + 2] << 16 | (uint)source[s + 1] << 8 | source[s];
                }

                break;
            case PixelLayout.Bgra32:
                for (int i = 0, s = 0; i < count; i++, s += 4)
                {
                    destination[i] = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(s, 4));
                }

                break;
            case PixelLayout.Rgb555:
            case PixelLayout.Argb1555:
                bool attribute = layout == PixelLayout.Argb1555;
                for (int i = 0, s = 0; i < count; i++, s += 2)
                {
                    destination[i] = FromWord((uint)(source[s] | source[s + 1] << 8), attribute);
                }

                break;
            case PixelLayout.Gray8:
                for (int i = 0; i < count; i++)
                {
                    destination[i] = 0xFF000000u | source[i] * 0x010101u;
                }

                break;
            case PixelLayout.GrayAlpha16:
                for (int i = 0, s = 0; i < count; i++, s += 2)
                {
                    destination[i] = (uint)source[s + 1] << 24 | source[s] * 0x010101u;
                }

                break;
            default:
                throw new InvalidOperationException("Colour-mapped pixels are converted through the palette.");
        }
    }

    /// <summary>Reads stored 8- or 16-bit palette indices; <paramref name="destination"/>.Length indices are read.</summary>
    public static void ReadIndices(PixelLayout layout, ReadOnlySpan<byte> source, Span<ushort> destination)
    {
        if (layout == PixelLayout.Index8)
        {
            for (int i = 0; i < destination.Length; i++)
            {
                destination[i] = source[i];
            }
        }
        else
        {
            for (int i = 0, s = 0; i < destination.Length; i++, s += 2)
            {
                destination[i] = (ushort)(source[s] | source[s + 1] << 8);
            }
        }
    }
}
