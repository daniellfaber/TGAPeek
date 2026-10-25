namespace TGAPeek.Core;

/// <summary>Upper-bound memory needed to decode one image, by component.</summary>
public sealed record TgaMemoryEstimate(
    long PixelBytes,
    long IndexBytes,
    long MaskBytes,
    long PaletteBytes,
    long MetadataBytes,
    long WorkingBytes)
{
    public long Subtotal => checked(PixelBytes + IndexBytes + MaskBytes + PaletteBytes + MetadataBytes + WorkingBytes);

    /// <summary>The subtotal plus a 10% margin for runtime overhead.</summary>
    public long Total => checked(Subtotal + Subtotal / 10);
}

/// <summary>
/// Estimates decode memory from the header alone, before anything large is allocated. The decoder
/// streams the file, so the file itself is never held in memory; a test keeps actual allocations
/// below this estimate. All arithmetic is checked: an overflow throws instead of wrapping.
/// </summary>
public static class TgaMemoryEstimator
{
    internal const int ArrayOverhead = 32;
    internal const int ReadBufferSize = 64 * 1024;
    internal const int MaxDeveloperPreviews = 256;
    internal const int DeveloperPreviewBytes = 64;

    public static TgaMemoryEstimate Estimate(TgaHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        checked
        {
            long width = header.Width;
            long height = header.Height;
            long pixels = width * height;
            long rowArrays = height * (ArrayOverhead + sizeof(long));
            bool colorMapped = header.Kind == TgaImageKind.ColorMapped;

            long pixelBytes = pixels * 4 + rowArrays + height * sizeof(int) + 2L * ArrayOverhead;
            long indexBytes = colorMapped ? pixels * (header.PixelDepth <= 8 ? 1 : 2) + rowArrays : 0;
            long maskBytes = 2 * (TgaBitMask.ByteSize(pixels) + ArrayOverhead);
            long paletteBytes = colorMapped ? (long)header.ColorMapLength * (4 + 4) + 2L * ArrayOverhead : 0;

            const long stampBytes = 255L * 255 * (4 + 4) + 1024;
            long metadataBytes = 256 + 2 * TgaDecoder.ExtensionAreaSize + stampBytes
                + height * 4 * 2 // scan-line table, raw and parsed
                + 2048 * 2 // colour-correction table, raw and parsed
                + ushort.MaxValue * (10L + 16L) // developer directory, raw and parsed
                + MaxDeveloperPreviews * (long)(DeveloperPreviewBytes + ArrayOverhead)
                + 64 * 1024;

            long workingBytes = ReadBufferSize
                + width * (4 + 4 + 2 + 1) // one stored row, decoded row, row indices and flags
                + height * 4 // row order
                + 64 * 1024;

            return new TgaMemoryEstimate(pixelBytes, indexBytes, maskBytes, paletteBytes, metadataBytes, workingBytes);
        }
    }

    /// <summary>Memory for displaying a viewport: the rendered buffer plus the window's back buffer.</summary>
    public static long EstimateViewportBytes(int width, int height) =>
        checked(2L * 4 * Math.Max(0, width) * Math.Max(0, height));
}
