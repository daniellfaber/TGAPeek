namespace TGAPeek.Core;

/// <summary>The viewer's alpha setting.</summary>
public enum AlphaMode
{
    /// <summary>Follow the file: TGA 2.0 attributes type first, then the header's attribute bits.</summary>
    Auto,

    /// <summary>Always apply stored alpha.</summary>
    Use,

    /// <summary>Never apply alpha; show colour channels opaque.</summary>
    Ignore,
}

/// <summary>How the alpha channel is applied when compositing for display.</summary>
public enum AlphaInterpretation
{
    Opaque,
    Straight,
    Premultiplied,
}

/// <summary>A display decision with a short summary for the status bar and a full explanation.</summary>
public sealed record AlphaDecision(AlphaInterpretation Interpretation, string Summary, string Explanation);

/// <summary>
/// Decides how alpha is shown. This never changes the decoded data, and every decision is explained
/// so nothing is applied silently. A 32-bit (or other alpha-capable) layout alone never implies alpha:
/// the file must declare it through the TGA 2.0 attributes type or the header's attribute bits.
/// </summary>
public static class AlphaPolicy
{
    public static AlphaDecision Resolve(TgaImage image, AlphaMode mode)
    {
        ArgumentNullException.ThrowIfNull(image);
        var alpha = image.Alpha;
        if (!alpha.HasStoredAlpha)
        {
            return new AlphaDecision(
                AlphaInterpretation.Opaque,
                "None stored",
                $"This file stores no alpha channel ({image.FormatDescription}). It is shown opaque, and the inspector reports alpha as 255 (not stored).");
        }

        string kind = alpha.Storage == TgaAlphaStorage.OneBit ? "1-bit alpha" : "8-bit alpha";
        var applied = alpha.AttributesType == 4 ? AlphaInterpretation.Premultiplied : AlphaInterpretation.Straight;
        string appliedName = applied == AlphaInterpretation.Premultiplied ? "premultiplied" : "straight";

        if (mode == AlphaMode.Ignore)
        {
            return new AlphaDecision(AlphaInterpretation.Opaque, $"{kind}, ignored", "Alpha is ignored because View ▸ Alpha ▸ Ignore alpha is selected.");
        }

        if (mode == AlphaMode.Use)
        {
            string note = alpha.AllZero ? " Every alpha value is 0, so the image is fully transparent." : "";
            return new AlphaDecision(applied, $"{kind}, applied ({appliedName})", $"Alpha is applied because View ▸ Alpha ▸ Use alpha is selected.{note}");
        }

        string reason;
        switch (alpha.AttributesType)
        {
            case 0:
                return new AlphaDecision(AlphaInterpretation.Opaque, $"{kind} stored, not applied", "The TGA 2.0 extension area says the file has no alpha data (attributes type 0). The stored values remain visible in the Alpha view.");
            case 1:
                return new AlphaDecision(AlphaInterpretation.Opaque, $"{kind} stored, not applied", "The TGA 2.0 extension area says the alpha data is undefined and can be ignored (attributes type 1). The stored values remain visible in the Alpha view.");
            case 2:
                return new AlphaDecision(AlphaInterpretation.Opaque, $"{kind} stored, not applied", "The TGA 2.0 extension area says the alpha data is undefined but should be retained (attributes type 2). It is kept, shown in the Alpha view and inspector, and not used as transparency. View ▸ Alpha ▸ Use alpha applies it.");
            case 3:
                reason = "The TGA 2.0 extension area says the file has useful alpha (attributes type 3).";
                break;
            case 4:
                reason = "The TGA 2.0 extension area says the alpha is premultiplied (attributes type 4).";
                break;
            default:
                if (alpha.DeclaredAttributeBits == 0)
                {
                    return new AlphaDecision(
                        AlphaInterpretation.Opaque,
                        $"{kind} stored, not applied",
                        "The header declares no alpha bits, so the stored alpha channel is not used as transparency; a 32-bit layout alone does not mean the file has alpha. View ▸ Alpha ▸ Use alpha applies it, and the Alpha view shows it.");
                }

                reason = $"The header declares {alpha.DeclaredAttributeBits} alpha bit(s).";
                break;
        }

        if (alpha.AllZero)
        {
            return new AlphaDecision(
                AlphaInterpretation.Opaque,
                "All alpha 0, shown opaque",
                reason + " However, every alpha value is 0, which usually means the channel is unused, so the image is shown opaque. This affects display only. View ▸ Alpha ▸ Use alpha shows it fully transparent.");
        }

        string opaqueNote = alpha.AllOpaque ? " Every alpha value is 255 (fully opaque)." : "";
        return new AlphaDecision(applied, $"{kind}, applied ({appliedName})", reason + opaqueNote);
    }
}
