using System.Buffers.Binary;

namespace TGAPeek.Core.Decoding;

/// <summary>
/// Reads the optional TGA 2.0 structures after the pixels have been decoded. Each structure is
/// validated on its own: its offset and size must lie between the end of the pixel data and the
/// footer. A structure that fails becomes Invalid with a reason and a warning; it can never
/// change the decoded pixels.
/// </summary>
internal sealed class OptionalStructureReader
{
    private readonly Stream _stream;
    private readonly long _basePosition;
    private readonly long _fileLength;
    private readonly TgaDecoder.Footer? _footer;
    private readonly TgaHeader _header;
    private readonly PixelLayout _layout;
    private readonly TgaPalette? _palette;
    private readonly long _pixelStart;
    private readonly long _pixelEnd;
    private readonly long _footerStart;
    private readonly WarningList _warnings;

    public OptionalStructureReader(
        Stream stream,
        long basePosition,
        long fileLength,
        TgaDecoder.Footer? footer,
        TgaHeader header,
        PixelLayout layout,
        TgaPalette? palette,
        long pixelStart,
        long pixelEnd,
        WarningList warnings)
    {
        _stream = stream;
        _basePosition = basePosition;
        _fileLength = fileLength;
        _footer = footer;
        _header = header;
        _layout = layout;
        _palette = palette;
        _pixelStart = pixelStart;
        _pixelEnd = pixelEnd;
        _footerStart = fileLength - TgaDecoder.FooterSize;
        _warnings = warnings;
    }

    public (TgaMetadata Metadata, byte? AttributesType) Read()
    {
        var metadata = new TgaMetadata { FileLength = _fileLength, PixelDataOffset = _pixelStart, PixelDataEnd = _pixelEnd };
        if (_footer is null)
        {
            return (metadata, null);
        }

        var extension = Guard(_footer.ExtensionOffset, ReadExtension);
        var developer = Guard(_footer.DeveloperDirectoryOffset, ReadDeveloperDirectory);
        var colorCorrection = TgaStructure<TgaColorCorrectionTable>.Absent;
        var stamp = TgaStructure<TgaPostageStamp>.Absent;
        var scanLines = TgaStructure<TgaScanLineTable>.Absent;
        byte? attributesType = null;

        if (extension.Value is { } area)
        {
            colorCorrection = Guard(area.ColorCorrectionOffset, ReadColorCorrection);
            stamp = Guard(area.PostageStampOffset, ReadPostageStamp);
            scanLines = Guard(area.ScanLineOffset, ReadScanLineTable);
            attributesType = InterpretAttributesType(area.AttributesType);
        }

        Report(extension, TgaWarningCode.ExtensionAreaInvalid, "extension area");
        Report(developer, TgaWarningCode.DeveloperDirectoryInvalid, "developer directory");
        Report(colorCorrection, TgaWarningCode.ColorCorrectionTableInvalid, "colour-correction table");
        Report(stamp, TgaWarningCode.PostageStampInvalid, "postage stamp");
        Report(scanLines, TgaWarningCode.ScanLineTableInvalid, "scan-line table");

        return (metadata with
        {
            HasFooter = true,
            FooterExtensionOffset = _footer.ExtensionOffset,
            FooterDeveloperDirectoryOffset = _footer.DeveloperDirectoryOffset,
            Extension = extension,
            DeveloperDirectory = developer,
            ColorCorrection = colorCorrection,
            PostageStamp = stamp,
            ScanLineTable = scanLines,
        }, attributesType);
    }

    private byte? InterpretAttributesType(byte type)
    {
        if (type > 4)
        {
            _warnings.Add(
                TgaWarningCode.UnknownAttributesType,
                $"The extension area's attributes type is {type}, which TGA 2.0 does not define; alpha is interpreted from the header instead.");
            return null;
        }

        if (type == 0 && _header.AttributeBits > 0)
        {
            _warnings.Add(
                TgaWarningCode.AttributesTypeConflict,
                $"The extension area says there is no alpha data, but the header declares {_header.AttributeBits} attribute bit(s); the extension area is followed.");
        }

        return type;
    }

    private void Report<T>(TgaStructure<T> structure, TgaWarningCode code, string name)
        where T : class
    {
        if (structure.Status == TgaStructureStatus.Invalid)
        {
            _warnings.Add(code, $"The TGA 2.0 {name} at offset {structure.Offset} was ignored because {structure.Problem}.");
        }
    }

    private static TgaStructure<T> Guard<T>(uint offset, Func<uint, TgaStructure<T>> read)
        where T : class
    {
        if (offset == 0)
        {
            return TgaStructure<T>.Absent;
        }

        try
        {
            return read(offset);
        }
        catch (IOException ex)
        {
            return TgaStructure<T>.Bad(offset, $"it could not be read ({ex.Message})");
        }
    }

    /// <summary>Null when [offset, offset + size) lies between the pixel data and the footer; otherwise why not.</summary>
    private string? RangeProblem(long offset, long size)
    {
        if (offset < _pixelEnd)
        {
            return "it overlaps the header, colour map or pixel data";
        }

        if (size < 0 || offset > _footerStart - size)
        {
            return "it extends past the end of the file's data";
        }

        return null;
    }

    private bool ReadAt(long offset, Span<byte> destination)
    {
        _stream.Seek(_basePosition + offset, SeekOrigin.Begin);
        return _stream.ReadAtLeast(destination, destination.Length, throwOnEndOfStream: false) == destination.Length;
    }

    private TgaStructure<TgaExtensionArea> ReadExtension(uint offset)
    {
        if (RangeProblem(offset, 2) is { } problem)
        {
            return TgaStructure<TgaExtensionArea>.Bad(offset, problem);
        }

        Span<byte> sizeBytes = stackalloc byte[2];
        if (!ReadAt(offset, sizeBytes))
        {
            return TgaStructure<TgaExtensionArea>.Bad(offset, "it could not be read");
        }

        int size = BinaryPrimitives.ReadUInt16LittleEndian(sizeBytes);
        if (size < TgaDecoder.ExtensionAreaSize)
        {
            return TgaStructure<TgaExtensionArea>.Bad(offset, $"its size field is {size}, but TGA 2.0 requires {TgaDecoder.ExtensionAreaSize}");
        }

        if (RangeProblem(offset, size) is { } sizeProblem)
        {
            return TgaStructure<TgaExtensionArea>.Bad(offset, sizeProblem);
        }

        var data = new byte[TgaDecoder.ExtensionAreaSize];
        if (!ReadAt(offset, data))
        {
            return TgaStructure<TgaExtensionArea>.Bad(offset, "it could not be read");
        }

        if (size > TgaDecoder.ExtensionAreaSize)
        {
            _warnings.Add(
                TgaWarningCode.ExtensionAreaLarger,
                $"The extension area is {size} bytes; the {TgaDecoder.ExtensionAreaSize} bytes defined by TGA 2.0 were read.");
        }

        ReadOnlySpan<byte> d = data;
        var comments = new string[4];
        for (int i = 0; i < comments.Length; i++)
        {
            comments[i] = TgaText.Field(d.Slice(43 + i * 81, 81));
        }

        var area = new TgaExtensionArea
        {
            Size = size,
            AuthorName = TgaText.Field(d.Slice(2, 41)),
            AuthorComments = comments,
            Month = U16(d, 367),
            Day = U16(d, 369),
            Year = U16(d, 371),
            Hour = U16(d, 373),
            Minute = U16(d, 375),
            Second = U16(d, 377),
            JobName = TgaText.Field(d.Slice(379, 41)),
            JobHours = U16(d, 420),
            JobMinutes = U16(d, 422),
            JobSeconds = U16(d, 424),
            SoftwareId = TgaText.Field(d.Slice(426, 41)),
            SoftwareVersionNumber = U16(d, 467),
            SoftwareVersionLetter = d[469],
            KeyColor = BinaryPrimitives.ReadUInt32LittleEndian(d[470..]),
            PixelAspectNumerator = U16(d, 474),
            PixelAspectDenominator = U16(d, 476),
            GammaNumerator = U16(d, 478),
            GammaDenominator = U16(d, 480),
            ColorCorrectionOffset = BinaryPrimitives.ReadUInt32LittleEndian(d[482..]),
            PostageStampOffset = BinaryPrimitives.ReadUInt32LittleEndian(d[486..]),
            ScanLineOffset = BinaryPrimitives.ReadUInt32LittleEndian(d[490..]),
            AttributesType = d[494],
        };
        return TgaStructure<TgaExtensionArea>.Ok(area, offset);
    }

    private TgaStructure<TgaDeveloperDirectory> ReadDeveloperDirectory(uint offset)
    {
        if (RangeProblem(offset, 2) is { } problem)
        {
            return TgaStructure<TgaDeveloperDirectory>.Bad(offset, problem);
        }

        Span<byte> countBytes = stackalloc byte[2];
        if (!ReadAt(offset, countBytes))
        {
            return TgaStructure<TgaDeveloperDirectory>.Bad(offset, "it could not be read");
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(countBytes);
        long size = 2 + 10L * count;
        if (RangeProblem(offset, size) is { } sizeProblem)
        {
            return TgaStructure<TgaDeveloperDirectory>.Bad(offset, $"its {count} entries do not fit ({sizeProblem})");
        }

        var raw = new byte[10 * count];
        if (!ReadAt(offset + 2L, raw))
        {
            return TgaStructure<TgaDeveloperDirectory>.Bad(offset, "it could not be read");
        }

        var entries = new TgaDeveloperEntry[count];
        var previews = new List<byte[]?>(Math.Min(count, TgaMemoryEstimator.MaxDeveloperPreviews));
        int invalid = 0;
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> e = raw.AsSpan(i * 10, 10);
            ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(e);
            uint fieldOffset = BinaryPrimitives.ReadUInt32LittleEndian(e[2..]);
            uint fieldSize = BinaryPrimitives.ReadUInt32LittleEndian(e[6..]);
            bool valid = (fieldOffset == 0 && fieldSize == 0) || RangeProblem(fieldOffset, fieldSize) is null;
            entries[i] = new TgaDeveloperEntry(tag, fieldOffset, fieldSize, valid);
            if (!valid)
            {
                invalid++;
            }

            if (i < TgaMemoryEstimator.MaxDeveloperPreviews)
            {
                byte[]? preview = null;
                if (valid && fieldSize > 0)
                {
                    preview = new byte[Math.Min(fieldSize, (uint)TgaMemoryEstimator.DeveloperPreviewBytes)];
                    if (!ReadAt(fieldOffset, preview))
                    {
                        preview = null;
                    }
                }

                previews.Add(preview);
            }
        }

        if (invalid > 0)
        {
            _warnings.Add(
                TgaWarningCode.DeveloperEntriesInvalid,
                $"{invalid} developer-directory entr{(invalid == 1 ? "y points" : "ies point")} outside the file's data; their contents were ignored.");
        }

        return TgaStructure<TgaDeveloperDirectory>.Ok(new TgaDeveloperDirectory(entries, previews), offset);
    }

    private TgaStructure<TgaColorCorrectionTable> ReadColorCorrection(uint offset)
    {
        const int size = 256 * 4 * 2;
        if (RangeProblem(offset, size) is { } problem)
        {
            return TgaStructure<TgaColorCorrectionTable>.Bad(offset, problem);
        }

        var raw = new byte[size];
        if (!ReadAt(offset, raw))
        {
            return TgaStructure<TgaColorCorrectionTable>.Bad(offset, "it could not be read");
        }

        var values = new ushort[256 * 4];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(i * 2));
        }

        return TgaStructure<TgaColorCorrectionTable>.Ok(new TgaColorCorrectionTable(values), offset);
    }

    /// <summary>
    /// Field 26: one byte width, one byte height, then pixels in the image's own format without
    /// compression. The spec does not give the stamp's orientation; the image's origin bits are used.
    /// </summary>
    private TgaStructure<TgaPostageStamp> ReadPostageStamp(uint offset)
    {
        if (RangeProblem(offset, 2) is { } problem)
        {
            return TgaStructure<TgaPostageStamp>.Bad(offset, problem);
        }

        Span<byte> size = stackalloc byte[2];
        if (!ReadAt(offset, size))
        {
            return TgaStructure<TgaPostageStamp>.Bad(offset, "it could not be read");
        }

        int width = size[0];
        int height = size[1];
        int bytesPerPixel = PixelConversion.BytesPerPixel(_layout);
        long dataSize = (long)width * height * bytesPerPixel;
        if (RangeProblem(offset + 2L, dataSize) is { } dataProblem)
        {
            return TgaStructure<TgaPostageStamp>.Bad(offset, $"its {width} × {height} pixels do not fit ({dataProblem})");
        }

        var raw = new byte[dataSize];
        if (!ReadAt(offset + 2L, raw))
        {
            return TgaStructure<TgaPostageStamp>.Bad(offset, "it could not be read");
        }

        var pixels = new uint[width * height];
        var row = new uint[width];
        var indices = new ushort[width];
        int invalid = 0;
        for (int k = 0; k < height; k++)
        {
            ReadOnlySpan<byte> stored = raw.AsSpan(k * width * bytesPerPixel, width * bytesPerPixel);
            if (_palette is null)
            {
                PixelConversion.ConvertDirect(_layout, stored, row);
            }
            else
            {
                PixelConversion.ReadIndices(_layout, stored, indices);
                for (int c = 0; c < width; c++)
                {
                    bool known = _palette.Contains(indices[c]);
                    row[c] = known ? _palette[indices[c]] : 0;
                    invalid += known ? 0 : 1;
                }
            }

            int y = _header.TopToBottom ? k : height - 1 - k;
            for (int c = 0; c < width; c++)
            {
                int x = _header.RightToLeft ? width - 1 - c : c;
                pixels[y * width + x] = row[c];
            }
        }

        return TgaStructure<TgaPostageStamp>.Ok(new TgaPostageStamp(width, height, pixels, invalid), offset);
    }

    /// <summary>Field 25: one 4-byte offset per stored row, in the order the rows were saved. Checked, never used to decode.</summary>
    private TgaStructure<TgaScanLineTable> ReadScanLineTable(uint offset)
    {
        int rows = _header.Height;
        long size = 4L * rows;
        if (RangeProblem(offset, size) is { } problem)
        {
            return TgaStructure<TgaScanLineTable>.Bad(offset, problem);
        }

        var raw = new byte[size];
        if (!ReadAt(offset, raw))
        {
            return TgaStructure<TgaScanLineTable>.Bad(offset, "it could not be read");
        }

        var offsets = new uint[rows];
        int mismatches = 0;
        long rowBytes = (long)_header.Width * PixelConversion.BytesPerPixel(_layout);
        for (int k = 0; k < rows; k++)
        {
            offsets[k] = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(k * 4));
            bool matches = _header.IsRunLengthEncoded
                ? offsets[k] >= _pixelStart && offsets[k] < _pixelEnd
                : offsets[k] == _pixelStart + k * rowBytes;
            mismatches += matches ? 0 : 1;
        }

        if (mismatches > 0)
        {
            _warnings.Add(
                TgaWarningCode.ScanLineTableMismatch,
                $"{mismatches} of {rows} scan-line table entries do not match the pixel data. The table is informational and is not used for decoding.");
        }

        return TgaStructure<TgaScanLineTable>.Ok(new TgaScanLineTable(offsets, mismatches), offset);
    }

    private static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
}
