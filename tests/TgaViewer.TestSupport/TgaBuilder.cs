using System.Text;

namespace TgaViewer.TestSupport;

/// <summary>A TGA file to write. Pixels are given in image order (y = 0 is the top row) as stored bytes.</summary>
public sealed class TgaSpec
{
    public byte ImageType { get; set; } = 2;
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;
    public byte PixelDepth { get; set; } = 24;
    public int AttributeBits { get; set; }
    public bool RightToLeft { get; set; }
    public bool TopToBottom { get; set; }

    /// <summary>0 none, 1 two-way, 2 four-way, 3 reserved.</summary>
    public int Interleave { get; set; }

    public ushort XOrigin { get; set; }
    public ushort YOrigin { get; set; }
    public byte[] ImageId { get; set; } = [];
    public byte? ColorMapTypeOverride { get; set; }
    public ushort? ColorMapLengthOverride { get; set; }
    public ushort ColorMapFirstIndex { get; set; }
    public byte ColorMapEntryBits { get; set; }

    /// <summary>Stored colour-map entries, or null for no colour map.</summary>
    public byte[][]? ColorMap { get; set; }

    /// <summary>Stored pixel bytes in image order: Pixels[y * Width + x].</summary>
    public byte[][] Pixels { get; set; } = [];

    /// <summary>When false, RLE packets stop at the end of each stored row.</summary>
    public bool RlePacketsMayCrossRows { get; set; } = true;

    public byte[] DataAfterPixels { get; set; } = [];

    public Tga2Spec? Tga2 { get; set; }

    public bool IsRle => ImageType is 9 or 10 or 11;

    public int BytesPerPixel => (PixelDepth + 7) / 8;

    public byte Descriptor => (byte)((AttributeBits & 0x0F) | (RightToLeft ? 0x10 : 0) | (TopToBottom ? 0x20 : 0) | (Interleave & 3) << 6);
}

/// <summary>TGA 2.0 structures to append after the pixel data.</summary>
public sealed class Tga2Spec
{
    public bool IncludeExtensionArea { get; set; } = true;
    public ushort ExtensionSizeField { get; set; } = 495;
    public string AuthorName { get; set; } = "";
    public string[] AuthorComments { get; set; } = [];
    public ushort[] Timestamp { get; set; } = new ushort[6];
    public string JobName { get; set; } = "";
    public ushort[] JobTime { get; set; } = new ushort[3];
    public string SoftwareId { get; set; } = "";
    public ushort SoftwareVersionNumber { get; set; }
    public byte SoftwareVersionLetter { get; set; } = (byte)' ';
    public uint KeyColor { get; set; }
    public ushort AspectNumerator { get; set; }
    public ushort AspectDenominator { get; set; }
    public ushort GammaNumerator { get; set; }
    public ushort GammaDenominator { get; set; }
    public byte AttributesType { get; set; }
    public ushort[]? ColorCorrectionTable { get; set; }
    public int StampWidth { get; set; }
    public int StampHeight { get; set; }

    /// <summary>Stored stamp pixel bytes in image order, or null for no stamp.</summary>
    public byte[][]? StampPixels { get; set; }

    public bool IncludeScanLineTable { get; set; }
    public List<(ushort Tag, byte[] Data)> DeveloperFields { get; set; } = [];
    public uint? ExtensionOffsetOverride { get; set; }
    public uint? DeveloperDirectoryOffsetOverride { get; set; }
    public uint? ColorCorrectionOffsetOverride { get; set; }
    public uint? PostageStampOffsetOverride { get; set; }
    public uint? ScanLineOffsetOverride { get; set; }
}

/// <summary>A written file and where its parts are.</summary>
public sealed record TgaBuild(
    byte[] Bytes,
    int PixelDataOffset,
    int PixelDataEnd,
    int ExtensionOffset,
    int DeveloperDirectoryOffset,
    int ColorCorrectionOffset,
    int PostageStampOffset,
    int ScanLineOffset,
    int FooterOffset);

/// <summary>Writes TGA files for tests and samples. Written separately from the decoder.</summary>
public static class TgaBuilder
{
    public static byte[] Bytes(TgaSpec spec) => Build(spec).Bytes;

    public static TgaBuild Build(TgaSpec spec)
    {
        if (spec.Pixels.Length != spec.Width * spec.Height)
        {
            throw new ArgumentException($"Expected {spec.Width * spec.Height} pixels, got {spec.Pixels.Length}.");
        }

        var o = new List<byte>(1024);
        int colorMapLength = spec.ColorMap?.Length ?? 0;
        o.Add((byte)spec.ImageId.Length);
        o.Add(spec.ColorMapTypeOverride ?? (byte)(spec.ColorMap is null ? 0 : 1));
        o.Add(spec.ImageType);
        U16(o, spec.ColorMapFirstIndex);
        U16(o, spec.ColorMapLengthOverride ?? (ushort)colorMapLength);
        o.Add(spec.ColorMapEntryBits);
        U16(o, spec.XOrigin);
        U16(o, spec.YOrigin);
        U16(o, (ushort)spec.Width);
        U16(o, (ushort)spec.Height);
        o.Add(spec.PixelDepth);
        o.Add(spec.Descriptor);
        o.AddRange(spec.ImageId);
        foreach (byte[] entry in spec.ColorMap ?? [])
        {
            o.AddRange(entry);
        }

        int pixelStart = o.Count;
        List<byte[]> stored = StoredOrder(spec);
        var rowStarts = new int[spec.Height];
        if (spec.IsRle)
        {
            EncodeRle(o, stored, spec.Width, spec.RlePacketsMayCrossRows, rowStarts);
        }
        else
        {
            for (int i = 0; i < stored.Count; i++)
            {
                if (i % spec.Width == 0)
                {
                    rowStarts[i / spec.Width] = o.Count;
                }

                o.AddRange(stored[i]);
            }
        }

        int pixelEnd = o.Count;
        o.AddRange(spec.DataAfterPixels);

        int extension = 0, developer = 0, colorCorrection = 0, stamp = 0, scanLines = 0, footer = 0;
        if (spec.Tga2 is { } t)
        {
            var fieldOffsets = new List<int>();
            foreach (var (_, data) in t.DeveloperFields)
            {
                fieldOffsets.Add(o.Count);
                o.AddRange(data);
            }

            if (t.DeveloperFields.Count > 0)
            {
                developer = o.Count;
                U16(o, (ushort)t.DeveloperFields.Count);
                for (int i = 0; i < t.DeveloperFields.Count; i++)
                {
                    U16(o, t.DeveloperFields[i].Tag);
                    U32(o, (uint)fieldOffsets[i]);
                    U32(o, (uint)t.DeveloperFields[i].Data.Length);
                }
            }

            if (t.ColorCorrectionTable is { } table)
            {
                colorCorrection = o.Count;
                foreach (ushort value in table)
                {
                    U16(o, value);
                }
            }

            if (t.StampPixels is { } stampPixels)
            {
                stamp = o.Count;
                o.Add((byte)t.StampWidth);
                o.Add((byte)t.StampHeight);
                for (int k = 0; k < t.StampHeight; k++)
                {
                    int y = spec.TopToBottom ? k : t.StampHeight - 1 - k;
                    for (int c = 0; c < t.StampWidth; c++)
                    {
                        int x = spec.RightToLeft ? t.StampWidth - 1 - c : c;
                        o.AddRange(stampPixels[y * t.StampWidth + x]);
                    }
                }
            }

            if (t.IncludeScanLineTable)
            {
                scanLines = o.Count;
                foreach (int start in rowStarts)
                {
                    U32(o, (uint)start);
                }
            }

            if (t.IncludeExtensionArea)
            {
                extension = o.Count;
                var area = new byte[Math.Max(495, (int)t.ExtensionSizeField)];
                Put16(area, 0, t.ExtensionSizeField);
                Text(area, 2, 41, t.AuthorName);
                for (int i = 0; i < 4; i++)
                {
                    Text(area, 43 + i * 81, 81, i < t.AuthorComments.Length ? t.AuthorComments[i] : "");
                }

                for (int i = 0; i < 6; i++)
                {
                    Put16(area, 367 + i * 2, t.Timestamp[i]);
                }

                Text(area, 379, 41, t.JobName);
                for (int i = 0; i < 3; i++)
                {
                    Put16(area, 420 + i * 2, t.JobTime[i]);
                }

                Text(area, 426, 41, t.SoftwareId);
                Put16(area, 467, t.SoftwareVersionNumber);
                area[469] = t.SoftwareVersionLetter;
                Put32(area, 470, t.KeyColor);
                Put16(area, 474, t.AspectNumerator);
                Put16(area, 476, t.AspectDenominator);
                Put16(area, 478, t.GammaNumerator);
                Put16(area, 480, t.GammaDenominator);
                Put32(area, 482, t.ColorCorrectionOffsetOverride ?? (uint)colorCorrection);
                Put32(area, 486, t.PostageStampOffsetOverride ?? (uint)stamp);
                Put32(area, 490, t.ScanLineOffsetOverride ?? (uint)scanLines);
                area[494] = t.AttributesType;
                o.AddRange(area);
            }

            footer = o.Count;
            U32(o, t.ExtensionOffsetOverride ?? (uint)extension);
            U32(o, t.DeveloperDirectoryOffsetOverride ?? (uint)developer);
            o.AddRange(Encoding.ASCII.GetBytes("TRUEVISION-XFILE."));
            o.Add(0);
        }

        return new TgaBuild(o.ToArray(), pixelStart, pixelEnd, extension, developer, colorCorrection, stamp, scanLines, footer);
    }

    /// <summary>
    /// The image row of each stored row, written as a direct port of netpbm tgatoppm's loop
    /// (TGA 1.0 interleaving), kept separate from the decoder's own implementation.
    /// </summary>
    public static int[] StoredRowOrder(int rows, int interleave, bool topToBottom)
    {
        var order = new int[rows];
        int truerow = 0;
        int baserow = 0;
        for (int row = 0; row < rows; ++row)
        {
            order[row] = topToBottom ? truerow : rows - truerow - 1;
            if (interleave == 2)
            {
                truerow += 4;
            }
            else if (interleave == 1)
            {
                truerow += 2;
            }
            else
            {
                ++truerow;
            }

            if (truerow >= rows)
            {
                truerow = ++baserow;
            }
        }

        return order;
    }

    private static List<byte[]> StoredOrder(TgaSpec spec)
    {
        int[] order = StoredRowOrder(spec.Height, spec.Interleave, spec.TopToBottom);
        var stored = new List<byte[]>(spec.Pixels.Length);
        for (int k = 0; k < spec.Height; k++)
        {
            int y = order[k];
            for (int c = 0; c < spec.Width; c++)
            {
                int x = spec.RightToLeft ? spec.Width - 1 - c : c;
                stored.Add(spec.Pixels[y * spec.Width + x]);
            }
        }

        return stored;
    }

    /// <summary>Greedy RLE: runs of 2+ identical pixels become run packets, everything else raw packets (at most 128 pixels each).</summary>
    private static void EncodeRle(List<byte> o, List<byte[]> pixels, int width, bool mayCrossRows, int[] rowStarts)
    {
        int n = pixels.Count;
        int i = 0;
        while (i < n)
        {
            int limit = Math.Min(128, mayCrossRows ? n - i : width - i % width);
            int run = 1;
            while (run < limit && pixels[i].AsSpan().SequenceEqual(pixels[i + run]))
            {
                run++;
            }

            int packetStart = o.Count;
            int count;
            if (run >= 2)
            {
                o.Add((byte)(0x80 | (run - 1)));
                o.AddRange(pixels[i]);
                count = run;
            }
            else
            {
                count = 1;
                while (count < limit)
                {
                    int j = i + count;
                    if (count + 1 < limit && pixels[j].AsSpan().SequenceEqual(pixels[j + 1]))
                    {
                        break;
                    }

                    count++;
                }

                o.Add((byte)(count - 1));
                for (int k = 0; k < count; k++)
                {
                    o.AddRange(pixels[i + k]);
                }
            }

            for (int row = (i + width - 1) / width; row < rowStarts.Length && row * width < i + count; row++)
            {
                rowStarts[row] = packetStart;
            }

            i += count;
        }
    }

    private static void U16(List<byte> o, ushort value)
    {
        o.Add((byte)value);
        o.Add((byte)(value >> 8));
    }

    private static void U32(List<byte> o, uint value)
    {
        for (int i = 0; i < 4; i++)
        {
            o.Add((byte)(value >> (8 * i)));
        }
    }

    private static void Put16(byte[] a, int offset, ushort value)
    {
        a[offset] = (byte)value;
        a[offset + 1] = (byte)(value >> 8);
    }

    private static void Put32(byte[] a, int offset, uint value)
    {
        for (int i = 0; i < 4; i++)
        {
            a[offset + i] = (byte)(value >> (8 * i));
        }
    }

    private static void Text(byte[] a, int offset, int length, string text)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(text);
        Array.Copy(bytes, 0, a, offset, Math.Min(bytes.Length, length - 1));
    }
}
