namespace TGAPeek.Core;

/// <summary>
/// Resource limits and behaviour switches for decoding. Every limit is checked before the
/// corresponding memory is allocated. The defaults accept every size the TGA header can
/// describe (up to 65,535 × 65,535); the memory budget is the limit that normally applies.
/// </summary>
public sealed record TgaDecodeOptions
{
    public static TgaDecodeOptions Default { get; } = new();

    /// <summary>Largest accepted width in pixels (default: the format maximum, 65,535).</summary>
    public int MaxWidth { get; init; } = ushort.MaxValue;

    /// <summary>Largest accepted height in pixels (default: the format maximum, 65,535).</summary>
    public int MaxHeight { get; init; } = ushort.MaxValue;

    /// <summary>Largest accepted width × height (default: no restriction beyond the format maximum).</summary>
    public long MaxPixelCount { get; init; } = (long)ushort.MaxValue * ushort.MaxValue;

    /// <summary>
    /// Upper bound on the estimated memory needed to decode one image: decoded pixels, palette
    /// indices, masks, palette, metadata and working buffers (default 4 GiB).
    /// </summary>
    public long MaxDecodeMemoryBytes { get; init; } = 4L << 30;

    /// <summary>
    /// When true, a file whose pixel data is truncated decodes to a partial image with the missing
    /// pixels marked. When false, truncation is an error.
    /// </summary>
    public bool AllowPartial { get; init; }
}
