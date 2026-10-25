namespace TGAPeek.Core;

/// <summary>Kinds of non-fatal findings reported while decoding.</summary>
public enum TgaWarningCode
{
    AttributeBitsMismatch,
    ColorMapFieldsIgnored,
    UnusedColorMap,
    ReservedColorMapTypeIgnored,
    RlePacketsCrossScanLines,
    RleOverrun,
    InvalidPaletteIndices,
    TruncatedPixelData,
    ExtensionAreaInvalid,
    ExtensionAreaLarger,
    UnknownAttributesType,
    AttributesTypeConflict,
    DeveloperDirectoryInvalid,
    DeveloperEntriesInvalid,
    ColorCorrectionTableInvalid,
    PostageStampInvalid,
    ScanLineTableInvalid,
    ScanLineTableMismatch,
}

/// <summary>A non-fatal finding about a decoded file.</summary>
public sealed record TgaWarning(TgaWarningCode Code, string Message)
{
    /// <summary>True for informational notes, such as an accepted compatibility fallback, that are not problems.</summary>
    public bool IsNote => Code is TgaWarningCode.RlePacketsCrossScanLines
        or TgaWarningCode.UnusedColorMap
        or TgaWarningCode.ExtensionAreaLarger;
}

/// <summary>Collects warnings, keeping only the first of each kind so a damaged file cannot flood the list.</summary>
internal sealed class WarningList
{
    private readonly List<TgaWarning> _items = [];
    private readonly HashSet<TgaWarningCode> _codes = [];

    public void Add(TgaWarningCode code, string message)
    {
        if (_codes.Add(code))
        {
            _items.Add(new TgaWarning(code, message));
        }
    }

    public IReadOnlyList<TgaWarning> ToList() => _items.ToArray();
}
