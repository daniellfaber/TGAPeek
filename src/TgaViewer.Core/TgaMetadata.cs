using System.Text;

namespace TgaViewer.Core;

public enum TgaStructureStatus
{
    Absent,
    Valid,
    Invalid,
}

/// <summary>An optional TGA 2.0 structure: absent, valid (with its value) or invalid (with the reason it was ignored).</summary>
public sealed record TgaStructure<T>
    where T : class
{
    public static TgaStructure<T> Absent { get; } = new() { Status = TgaStructureStatus.Absent };

    public TgaStructureStatus Status { get; init; }

    public T? Value { get; init; }

    public long Offset { get; init; }

    /// <summary>Why an invalid structure was ignored, phrased to follow "because".</summary>
    public string? Problem { get; init; }

    internal static TgaStructure<T> Ok(T value, long offset) =>
        new() { Status = TgaStructureStatus.Valid, Value = value, Offset = offset };

    internal static TgaStructure<T> Bad(long offset, string problem) =>
        new() { Status = TgaStructureStatus.Invalid, Offset = offset, Problem = problem };
}

/// <summary>The TGA 2.0 extension area (fields 10–24), exactly as stored apart from text clean-up.</summary>
public sealed record TgaExtensionArea
{
    public int Size { get; init; }
    public string AuthorName { get; init; } = "";
    public IReadOnlyList<string> AuthorComments { get; init; } = [];
    public ushort Month { get; init; }
    public ushort Day { get; init; }
    public ushort Year { get; init; }
    public ushort Hour { get; init; }
    public ushort Minute { get; init; }
    public ushort Second { get; init; }
    public string JobName { get; init; } = "";
    public ushort JobHours { get; init; }
    public ushort JobMinutes { get; init; }
    public ushort JobSeconds { get; init; }
    public string SoftwareId { get; init; } = "";
    public ushort SoftwareVersionNumber { get; init; }
    public byte SoftwareVersionLetter { get; init; }

    /// <summary>Key (background) colour as stored: 0xAARRGGBB.</summary>
    public uint KeyColor { get; init; }

    public ushort PixelAspectNumerator { get; init; }
    public ushort PixelAspectDenominator { get; init; }
    public ushort GammaNumerator { get; init; }
    public ushort GammaDenominator { get; init; }
    public uint ColorCorrectionOffset { get; init; }
    public uint PostageStampOffset { get; init; }
    public uint ScanLineOffset { get; init; }
    public byte AttributesType { get; init; }

    /// <summary>Gamma value, or null when not specified (denominator 0).</summary>
    public double? Gamma => GammaDenominator == 0 ? null : (double)GammaNumerator / GammaDenominator;

    /// <summary>Pixel aspect ratio (width / height), or null when not specified.</summary>
    public double? PixelAspectRatio => PixelAspectDenominator == 0 ? null : (double)PixelAspectNumerator / PixelAspectDenominator;

    public string? Timestamp => Year == 0 && Month == 0 && Day == 0 && Hour == 0 && Minute == 0 && Second == 0
        ? null
        : $"{Year:D4}-{Month:D2}-{Day:D2} {Hour:D2}:{Minute:D2}:{Second:D2}";

    public string? JobTime => JobHours == 0 && JobMinutes == 0 && JobSeconds == 0
        ? null
        : $"{JobHours}:{JobMinutes:D2}:{JobSeconds:D2}";

    public string? SoftwareVersion
    {
        get
        {
            bool hasLetter = SoftwareVersionLetter is not (0 or (byte)' ');
            if (SoftwareVersionNumber == 0 && !hasLetter)
            {
                return null;
            }

            string letter = hasLetter ? TgaText.Field([SoftwareVersionLetter]) : "";
            return $"{SoftwareVersionNumber / 100}.{SoftwareVersionNumber % 100:D2}{letter}";
        }
    }
}

/// <summary>One developer-directory entry (tag, offset, size). Its data is never interpreted.</summary>
public readonly record struct TgaDeveloperEntry(ushort Tag, uint Offset, uint Size, bool IsValid);

/// <summary>The developer directory and up to the first 64 bytes of the first 256 entries' data.</summary>
public sealed record TgaDeveloperDirectory(IReadOnlyList<TgaDeveloperEntry> Entries, IReadOnlyList<byte[]?> Previews);

/// <summary>The colour-correction table: 256 entries of A, R, G, B (0–65535). Shown, never applied.</summary>
public sealed record TgaColorCorrectionTable(IReadOnlyList<ushort> Values);

/// <summary>The postage-stamp (thumbnail) image, decoded to 0xAARRGGBB, top row first.</summary>
public sealed record TgaPostageStamp(int Width, int Height, IReadOnlyList<uint> Pixels, int InvalidIndexCount);

/// <summary>The scan-line table: one file offset per stored row. Checked, never used for decoding.</summary>
public sealed record TgaScanLineTable(IReadOnlyList<uint> Offsets, int MismatchCount);

/// <summary>Everything in the file apart from the pixels.</summary>
public sealed record TgaMetadata
{
    public byte[] ImageId { get; init; } = [];

    /// <summary>Length of the file, or -1 when the stream could not report it.</summary>
    public long FileLength { get; init; } = -1;

    public long PixelDataOffset { get; init; }

    /// <summary>Offset just past the last byte of pixel data the decoder used.</summary>
    public long PixelDataEnd { get; init; }

    public bool HasFooter { get; init; }

    public uint FooterExtensionOffset { get; init; }

    public uint FooterDeveloperDirectoryOffset { get; init; }

    /// <summary>A true-colour or greyscale file included a colour map, which was skipped.</summary>
    public bool HasUnusedColorMap { get; init; }

    public TgaStructure<TgaExtensionArea> Extension { get; init; } = TgaStructure<TgaExtensionArea>.Absent;

    public TgaStructure<TgaDeveloperDirectory> DeveloperDirectory { get; init; } = TgaStructure<TgaDeveloperDirectory>.Absent;

    public TgaStructure<TgaColorCorrectionTable> ColorCorrection { get; init; } = TgaStructure<TgaColorCorrectionTable>.Absent;

    public TgaStructure<TgaPostageStamp> PostageStamp { get; init; } = TgaStructure<TgaPostageStamp>.Absent;

    public TgaStructure<TgaScanLineTable> ScanLineTable { get; init; } = TgaStructure<TgaScanLineTable>.Absent;

    public string ImageIdText => TgaText.Describe(ImageId);
}

/// <summary>Turns stored bytes into safe display text.</summary>
public static class TgaText
{
    /// <summary>A fixed-length text field: stops at the first NUL, shows bytes as Latin-1, replaces control characters with '?'.</summary>
    public static string Field(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        if (end >= 0)
        {
            bytes = bytes[..end];
        }

        var text = new StringBuilder(bytes.Length);
        foreach (byte b in bytes)
        {
            text.Append(b < 0x20 || (b >= 0x7F && b < 0xA0) ? '?' : (char)b);
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>Arbitrary data (such as the image ID): quoted text when printable, otherwise a short hex dump.</summary>
    public static string Describe(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return "(none)";
        }

        ReadOnlySpan<byte> trimmed = data.TrimEnd((byte)0);
        bool printable = !trimmed.IsEmpty;
        foreach (byte b in trimmed)
        {
            if (b < 0x20 && b is not ((byte)'\t' or (byte)'\r' or (byte)'\n'))
            {
                printable = false;
                break;
            }
        }

        if (printable)
        {
            return "\"" + Field(trimmed).Replace('\t', ' ') + "\"";
        }

        int shown = Math.Min(data.Length, 64);
        string hex = Convert.ToHexString(data[..shown]);
        var spaced = new StringBuilder(hex.Length * 3 / 2);
        for (int i = 0; i < hex.Length; i += 2)
        {
            if (i > 0)
            {
                spaced.Append(' ');
            }

            spaced.Append(hex, i, 2);
        }

        return $"{data.Length} bytes: {spaced}{(data.Length > shown ? " …" : "")}";
    }
}

/// <summary>Formats byte counts for messages.</summary>
public static class ByteSize
{
    public static string Format(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (Math.Abs(value) >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes:N0} bytes" : $"{value:0.#} {units[unit]}";
    }
}
