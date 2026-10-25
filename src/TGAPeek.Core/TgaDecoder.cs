using System.Buffers.Binary;
using TGAPeek.Core.Decoding;

namespace TGAPeek.Core;

/// <summary>The header of a file, checked against the decode options, and the memory a full decode would need.</summary>
public sealed record TgaProbe(TgaHeader Header, TgaMemoryEstimate Memory);

/// <summary>
/// Decodes TGA images (TGA 1.0 and 2.0). Input is treated as untrusted: reads are bounded, sizes
/// use 64-bit arithmetic and limits are checked before large allocations. Problems with the file's
/// content are reported only as <see cref="TgaException"/> subtypes; I/O errors from the stream and
/// cancellation propagate unchanged.
/// </summary>
public static class TgaDecoder
{
    internal const int FooterSize = 26;
    internal const int ExtensionAreaSize = 495;

    private static ReadOnlySpan<byte> Signature => "TRUEVISION-XFILE.\0"u8;

    /// <summary>Reads and validates only the header, so memory can be checked before decoding.</summary>
    public static TgaProbe Probe(Stream stream, TgaDecodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> bytes = stackalloc byte[TgaHeader.Size];
        int read = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (read < TgaHeader.Size)
        {
            throw TruncatedHeader(read);
        }

        var header = TgaHeader.Parse(bytes);
        var memory = Validate(header, options ?? TgaDecodeOptions.Default, new WarningList());
        return new TgaProbe(header, memory);
    }

    public static TgaImage Decode(byte[] data, TgaDecodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var stream = new MemoryStream(data, writable: false);
        return Decode(stream, options, cancellationToken);
    }

    /// <summary>Decodes the TGA image that starts at the stream's current position and runs to its end.</summary>
    public static TgaImage Decode(Stream stream, TgaDecodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable.", nameof(stream));
        }

        options ??= TgaDecodeOptions.Default;
        var warnings = new WarningList();

        long basePosition = stream.CanSeek ? stream.Position : 0;
        long fileLength = stream.CanSeek ? stream.Length - basePosition : -1;
        Footer? footer = fileLength >= 0 ? TryReadFooter(stream, basePosition, fileLength) : null;

        // Pixel data can never extend into a valid TGA 2.0 footer.
        long readLimit = footer is not null ? fileLength - FooterSize : fileLength >= 0 ? fileLength : long.MaxValue;
        var reader = new TgaStreamReader(stream, readLimit);

        Span<byte> headerBytes = stackalloc byte[TgaHeader.Size];
        int headerRead = reader.Read(headerBytes);
        if (headerRead < TgaHeader.Size)
        {
            throw TruncatedHeader(headerRead);
        }

        var header = TgaHeader.Parse(headerBytes);
        var memory = Validate(header, options, warnings);
        var kind = header.Kind!.Value;

        var imageId = new byte[header.IdLength];
        if (reader.Read(imageId) < imageId.Length)
        {
            throw new TgaInvalidFileException(
                TgaErrorReason.TruncatedImageId,
                $"The file ends inside its {header.IdLength}-byte image ID field.",
                reader.Position);
        }

        TgaPalette? palette = null;
        bool unusedColorMap = false;
        if (header.ColorMapType == 1)
        {
            if (kind == TgaImageKind.ColorMapped)
            {
                palette = ReadPalette(reader, header);
            }
            else
            {
                long length = header.ColorMapByteLength;
                if (reader.Skip(length) < length)
                {
                    throw new TgaInvalidFileException(TgaErrorReason.TruncatedColorMap, "The file ends inside the colour map.", reader.Position);
                }

                unusedColorMap = header.ColorMapLength > 0;
                if (unusedColorMap)
                {
                    warnings.Add(
                        TgaWarningCode.UnusedColorMap,
                        $"The file includes a {header.ColorMapLength}-entry colour map, which {(kind == TgaImageKind.TrueColor ? "true-colour" : "greyscale")} pixels do not use; it was skipped.");
                }
            }
        }

        long pixelStart = reader.Position;
        var layout = PixelConversion.LayoutFor(header);
        int bytesPerPixel = PixelConversion.BytesPerPixel(layout);
        var sink = new PixelSink(header, layout, palette);
        var rle = default(RleFindings);
        if (header.IsRunLengthEncoded)
        {
            rle = DecodeRle(reader, sink, header.PixelCount, header.Width, bytesPerPixel, cancellationToken);
        }
        else
        {
            DecodeUncompressed(reader, sink, header.Width, header.Height, bytesPerPixel, cancellationToken);
        }

        long pixelEnd = reader.Position;
        if (!sink.IsComplete)
        {
            long decoded = sink.DecodedPixels;
            string message = $"The file ends after {decoded:N0} of {header.PixelCount:N0} pixels ({(double)decoded / header.PixelCount:P0}).";
            if (!options.AllowPartial)
            {
                throw new TgaInvalidFileException(TgaErrorReason.TruncatedPixelData, message, pixelEnd);
            }

            sink.Finish();
            warnings.Add(TgaWarningCode.TruncatedPixelData, message + " The missing pixels are marked.");
        }

        if (rle.CrossesScanLines)
        {
            warnings.Add(
                TgaWarningCode.RlePacketsCrossScanLines,
                "Some RLE packets continue from one row into the next. TGA 2.0 says packets should not do this, but the data is unambiguous, so it was decoded as one continuous stream.");
        }

        if (rle.Overrun)
        {
            warnings.Add(TgaWarningCode.RleOverrun, "The RLE data describes more pixels than the image has; the extra pixels were ignored.");
        }

        if (sink.InvalidPixels > 0)
        {
            warnings.Add(
                TgaWarningCode.InvalidPaletteIndices,
                $"{sink.InvalidPixels:N0} pixel(s) use palette indices outside the colour map (valid: {palette!.FirstIndex}–{palette.LastIndex}). They have no colour and are marked.");
        }

        var structures = new OptionalStructureReader(stream, basePosition, fileLength, footer, header, layout, palette, pixelStart, pixelEnd, warnings).Read();
        var storage = AlphaStorageFor(layout, palette);
        var alpha = new TgaAlphaInfo
        {
            Storage = storage,
            DeclaredAttributeBits = header.AttributeBits,
            AttributesType = structures.AttributesType,
            AllZero = storage != TgaAlphaStorage.None && sink.AllAlphaZero,
            AllOpaque = storage == TgaAlphaStorage.None || sink.AllAlphaOpaque,
        };

        var metadata = structures.Metadata with { ImageId = imageId, HasUnusedColorMap = unusedColorMap };
        return new TgaImage(
            header,
            sink.Rows,
            sink.Indices8,
            sink.Indices16,
            sink.StoredRowOfImageRow,
            palette,
            sink.MissingMask,
            sink.InvalidMask,
            sink.DecodedPixels,
            alpha,
            metadata,
            warnings.ToList(),
            memory.PixelBytes + memory.IndexBytes + memory.PaletteBytes);
    }

    private static TgaMemoryEstimate Validate(TgaHeader h, TgaDecodeOptions options, WarningList warnings)
    {
        switch (h.ImageType)
        {
            case 0:
                throw new TgaUnsupportedFormatException(TgaErrorReason.NoImageData, "The file contains no image data (TGA image type 0).", 2);
            case 1 or 2 or 3 or 9 or 10 or 11:
                break;
            case 32 or 33:
                throw new TgaUnsupportedFormatException(
                    TgaErrorReason.UnsupportedCompression,
                    $"TGA image type {h.ImageType} (Huffman and delta compressed colour-mapped data) is not supported.",
                    2);
            default:
                throw new TgaInvalidFileException(
                    TgaErrorReason.UnknownImageType,
                    $"The header gives image type {h.ImageType}, which TGA does not define. The file is probably not a TGA image.",
                    2);
        }

        if (h.Width == 0 || h.Height == 0)
        {
            throw new TgaInvalidFileException(TgaErrorReason.ZeroDimensions, $"The header gives a size of {h.Width} × {h.Height} pixels, so there is no image.", 12);
        }

        var kind = h.Kind!.Value;
        switch (kind)
        {
            case TgaImageKind.TrueColor when h.PixelDepth is not (15 or 16 or 24 or 32):
                throw new TgaUnsupportedFormatException(
                    TgaErrorReason.UnsupportedPixelDepth,
                    $"{h.PixelDepth}-bit true-colour pixels are not supported (supported: 15, 16, 24 and 32 bits).",
                    16);
            case TgaImageKind.Grayscale when h.PixelDepth is not (8 or 16):
                throw new TgaUnsupportedFormatException(
                    TgaErrorReason.UnsupportedPixelDepth,
                    $"{h.PixelDepth}-bit greyscale pixels are not supported (supported: 8 bits, and 16 bits with alpha).",
                    16);
            case TgaImageKind.ColorMapped:
                if (h.PixelDepth is not (8 or 16))
                {
                    throw new TgaUnsupportedFormatException(
                        TgaErrorReason.UnsupportedPixelDepth,
                        $"{h.PixelDepth}-bit colour-map indices are not supported (supported: 8 and 16 bits).",
                        16);
                }

                if (h.ColorMapType != 1)
                {
                    throw new TgaInvalidFileException(TgaErrorReason.MissingColorMap, "The image is colour-mapped, but the header says the file has no colour map.", 1);
                }

                if (h.ColorMapLength == 0)
                {
                    throw new TgaInvalidFileException(TgaErrorReason.EmptyColorMap, "The image is colour-mapped, but its colour map has no entries.", 5);
                }

                if (h.ColorMapEntryBits is not (15 or 16 or 24 or 32))
                {
                    throw new TgaUnsupportedFormatException(
                        TgaErrorReason.UnsupportedColorMapEntrySize,
                        $"{h.ColorMapEntryBits}-bit colour-map entries are not supported (supported: 15, 16, 24 and 32 bits).",
                        7);
                }

                break;
        }

        if (kind != TgaImageKind.ColorMapped)
        {
            bool fieldsSet = h.ColorMapFirstIndex != 0 || h.ColorMapLength != 0 || h.ColorMapEntryBits != 0;
            if (h.ColorMapType == 0 && fieldsSet)
            {
                warnings.Add(TgaWarningCode.ColorMapFieldsIgnored, "The header says there is no colour map but fills in the colour-map fields; they were ignored.");
            }
            else if (h.ColorMapType > 1)
            {
                if (fieldsSet)
                {
                    throw new TgaUnsupportedFormatException(
                        TgaErrorReason.ReservedColorMapType,
                        $"Colour-map type {h.ColorMapType} is reserved or developer-defined, so the position of the pixel data cannot be determined.",
                        1);
                }

                warnings.Add(
                    TgaWarningCode.ReservedColorMapTypeIgnored,
                    $"Colour-map type {h.ColorMapType} is reserved or developer-defined; the file has no colour-map data, so it was ignored.");
            }
        }

        if (h.Interleave == TgaInterleave.Reserved)
        {
            throw new TgaUnsupportedFormatException(
                TgaErrorReason.ReservedInterleave,
                "The image descriptor uses the reserved interleaving value (bits 7–6 = 11), so the row order is undefined.",
                17);
        }

        if (h.Width > options.MaxWidth || h.Height > options.MaxHeight)
        {
            bool tooWide = h.Width > options.MaxWidth;
            throw new TgaLimitExceededException(
                TgaErrorReason.DimensionsTooLarge,
                $"The image is {h.Width} × {h.Height} pixels, larger than the configured limit of {options.MaxWidth} × {options.MaxHeight}.",
                tooWide ? h.Width : h.Height,
                tooWide ? options.MaxWidth : options.MaxHeight);
        }

        if (h.PixelCount > options.MaxPixelCount)
        {
            throw new TgaLimitExceededException(
                TgaErrorReason.PixelCountTooLarge,
                $"The image has {h.PixelCount:N0} pixels, more than the configured limit of {options.MaxPixelCount:N0}.",
                h.PixelCount,
                options.MaxPixelCount);
        }

        var memory = TgaMemoryEstimator.Estimate(h);
        if (memory.Total > options.MaxDecodeMemoryBytes)
        {
            throw new TgaLimitExceededException(
                TgaErrorReason.MemoryBudgetExceeded,
                $"Decoding this {h.Width} × {h.Height} image needs about {ByteSize.Format(memory.Total)}, more than the memory budget of {ByteSize.Format(options.MaxDecodeMemoryBytes)}.",
                memory.Total,
                options.MaxDecodeMemoryBytes);
        }

        CheckAttributeBits(h, warnings);
        return memory;
    }

    private static void CheckAttributeBits(TgaHeader h, WarningList warnings)
    {
        int bits = h.AttributeBits;
        bool colorMapped = h.Kind == TgaImageKind.ColorMapped;
        int depth = colorMapped ? h.ColorMapEntryBits : h.PixelDepth;
        bool grey = h.Kind == TgaImageKind.Grayscale;
        bool consistent = (grey, depth) switch
        {
            (false, 32) or (true, 16) => bits is 0 or 8,
            (false, 16) => bits is 0 or 1,
            _ => bits == 0,
        };

        if (!consistent)
        {
            string what = colorMapped ? $"{depth}-bit colour-map entries" : $"{depth}-bit pixels";
            warnings.Add(
                TgaWarningCode.AttributeBitsMismatch,
                $"The header declares {bits} attribute (alpha) bit(s), which does not match its {what}. The pixels were decoded as stored.");
        }
    }

    private static TgaPalette ReadPalette(TgaStreamReader reader, TgaHeader header)
    {
        int entryBytes = header.ColorMapEntryBytes;
        var raw = new byte[header.ColorMapLength * entryBytes];
        if (reader.Read(raw) < raw.Length)
        {
            throw new TgaInvalidFileException(
                TgaErrorReason.TruncatedColorMap,
                $"The file ends inside its {header.ColorMapLength}-entry colour map.",
                reader.Position);
        }

        var entries = new uint[header.ColorMapLength];
        for (int i = 0; i < entries.Length; i++)
        {
            entries[i] = PixelConversion.FromPaletteEntry(raw.AsSpan(i * entryBytes, entryBytes), header.ColorMapEntryBits);
        }

        return new TgaPalette(header.ColorMapFirstIndex, header.ColorMapEntryBits, entries);
    }

    private static void DecodeUncompressed(TgaStreamReader reader, PixelSink sink, int width, int height, int bytesPerPixel, CancellationToken cancellationToken)
    {
        var rowBytes = new byte[width * bytesPerPixel];
        for (int k = 0; k < height; k++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = reader.Read(rowBytes);
            int pixels = read / bytesPerPixel;
            sink.Append(rowBytes.AsSpan(0, pixels * bytesPerPixel), pixels);
            if (pixels < width)
            {
                return;
            }
        }
    }

    private readonly record struct RleFindings(bool CrossesScanLines, bool Overrun);

    /// <summary>
    /// RLE packets (field 5.2): a header byte, then one pixel repeated (run packet, bit 7 set) or
    /// count literal pixels (raw packet). Counts are pixels, not bytes. Packets are decoded as one
    /// continuous stream, so a packet that continues into the next stored row is handled; pixels
    /// beyond the end of the image are ignored.
    /// </summary>
    private static RleFindings DecodeRle(TgaStreamReader reader, PixelSink sink, long pixelCount, int width, int bytesPerPixel, CancellationToken cancellationToken)
    {
        var value = new byte[bytesPerPixel];
        var chunk = new byte[width * bytesPerPixel];
        long remaining = pixelCount;
        bool crosses = false;
        bool overrun = false;
        int packets = 0;
        while (remaining > 0)
        {
            if ((++packets & 0xFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int packetHeader = reader.ReadByte();
            if (packetHeader < 0)
            {
                break;
            }

            int count = (packetHeader & 0x7F) + 1;
            if (count > remaining)
            {
                overrun = true;
                count = (int)remaining;
            }

            if (count > sink.RowRemaining)
            {
                crosses = true;
            }

            if ((packetHeader & 0x80) != 0)
            {
                if (reader.Read(value) < bytesPerPixel)
                {
                    break;
                }

                sink.AppendRun(value, count);
            }
            else
            {
                int left = count;
                while (left > 0)
                {
                    int n = Math.Min(left, sink.RowRemaining);
                    int read = reader.Read(chunk.AsSpan(0, n * bytesPerPixel));
                    int pixels = read / bytesPerPixel;
                    sink.Append(chunk.AsSpan(0, pixels * bytesPerPixel), pixels);
                    if (pixels < n)
                    {
                        return new RleFindings(crosses, overrun);
                    }

                    left -= n;
                }
            }

            remaining -= count;
        }

        return new RleFindings(crosses, overrun);
    }

    private static TgaAlphaStorage AlphaStorageFor(PixelLayout layout, TgaPalette? palette) => layout switch
    {
        PixelLayout.Bgra32 or PixelLayout.GrayAlpha16 => TgaAlphaStorage.EightBit,
        PixelLayout.Argb1555 => TgaAlphaStorage.OneBit,
        PixelLayout.Index8 or PixelLayout.Index16 => palette!.EntryBits switch
        {
            32 => TgaAlphaStorage.EightBit,
            16 => TgaAlphaStorage.OneBit,
            _ => TgaAlphaStorage.None,
        },
        _ => TgaAlphaStorage.None,
    };

    private static TgaInvalidFileException TruncatedHeader(int length) => new(
        TgaErrorReason.TruncatedHeader,
        length == 0 ? "The file is empty." : $"The file is only {length} byte(s) long, shorter than the 18-byte TGA header.",
        length);

    internal sealed record Footer(uint ExtensionOffset, uint DeveloperDirectoryOffset);

    /// <summary>The TGA 2.0 footer (last 26 bytes): two offsets, then "TRUEVISION-XFILE.\0".</summary>
    private static Footer? TryReadFooter(Stream stream, long basePosition, long length)
    {
        if (length < TgaHeader.Size + FooterSize)
        {
            return null;
        }

        Span<byte> bytes = stackalloc byte[FooterSize];
        int read;
        stream.Seek(basePosition + length - FooterSize, SeekOrigin.Begin);
        try
        {
            read = stream.ReadAtLeast(bytes, FooterSize, throwOnEndOfStream: false);
        }
        finally
        {
            stream.Seek(basePosition, SeekOrigin.Begin);
        }

        if (read < FooterSize || !bytes[8..].SequenceEqual(Signature))
        {
            return null;
        }

        return new Footer(BinaryPrimitives.ReadUInt32LittleEndian(bytes), BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]));
    }
}
