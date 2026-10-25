namespace TgaViewer.Core;

/// <summary>Why a file could not be decoded.</summary>
public enum TgaErrorReason
{
    TruncatedHeader,
    UnknownImageType,
    NoImageData,
    UnsupportedCompression,
    ZeroDimensions,
    UnsupportedPixelDepth,
    MissingColorMap,
    EmptyColorMap,
    UnsupportedColorMapEntrySize,
    ReservedColorMapType,
    ReservedInterleave,
    TruncatedImageId,
    TruncatedColorMap,
    TruncatedPixelData,
    DimensionsTooLarge,
    PixelCountTooLarge,
    MemoryBudgetExceeded,
}

/// <summary>Base class for every problem the decoder reports about file content.</summary>
public abstract class TgaException : Exception
{
    private protected TgaException(TgaErrorReason reason, string message, long? offset)
        : base(message)
    {
        Reason = reason;
        Offset = offset;
    }

    public TgaErrorReason Reason { get; }

    /// <summary>Byte offset in the file where the problem was found, when known.</summary>
    public long? Offset { get; }
}

/// <summary>The data breaks the TGA format, so it cannot be decoded.</summary>
public sealed class TgaInvalidFileException : TgaException
{
    public TgaInvalidFileException(TgaErrorReason reason, string message, long? offset = null)
        : base(reason, message, offset)
    {
    }
}

/// <summary>The file is a recognisable TGA variant that this viewer does not decode.</summary>
public sealed class TgaUnsupportedFormatException : TgaException
{
    public TgaUnsupportedFormatException(TgaErrorReason reason, string message, long? offset = null)
        : base(reason, message, offset)
    {
    }
}

/// <summary>The file appears valid, but decoding it would exceed a configured resource limit.</summary>
public sealed class TgaLimitExceededException : TgaException
{
    public TgaLimitExceededException(TgaErrorReason reason, string message, long required, long limit)
        : base(reason, message, null)
    {
        Required = required;
        Limit = limit;
    }

    public long Required { get; }

    public long Limit { get; }
}
