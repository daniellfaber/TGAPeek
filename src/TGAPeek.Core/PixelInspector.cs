using System.Globalization;
using System.Text;

namespace TGAPeek.Core;

/// <summary>Describes one pixel from the decoded source data, never from the screen.</summary>
public static class PixelInspector
{
    /// <summary>One line for the status bar, for example "x 12, y 34 · R 255 G 128 B 0 A 255".</summary>
    public static string Summarise(TgaImage image, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(image);
        string position = $"x {x}, y {y}";
        if (image.IsMissing(x, y))
        {
            return position + " · missing (file truncated)";
        }

        if (image.IsInvalidIndex(x, y) && image.TryGetPaletteIndex(x, y, out int bad))
        {
            return $"{position} · invalid palette index {bad}";
        }

        uint p = image.GetPixel(x, y);
        string alpha = image.Alpha.HasStoredAlpha ? $" A {p >> 24}" : " A — (none stored)";
        string index = image.TryGetPaletteIndex(x, y, out int i) ? $" · index {i}" : "";
        return $"{position} · R {(p >> 16) & 0xFF} G {(p >> 8) & 0xFF} B {p & 0xFF}{alpha}{index}";
    }

    /// <summary>A full description of the pixel's stored and decoded values.</summary>
    public static string Describe(TgaImage image, int x, int y, AlphaDecision decision)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(decision);
        var text = new StringBuilder();
        var (row, column) = image.GetStoredPosition(x, y);
        text.AppendLine(CultureInfo.InvariantCulture, $"Pixel x {x}, y {y} (from the top-left)");
        text.AppendLine(CultureInfo.InvariantCulture, $"Stored at row {row}, column {column} of the pixel data");

        if (image.IsMissing(x, y))
        {
            text.AppendLine("Missing: the file ends before this pixel.");
            return text.ToString();
        }

        bool hasIndex = image.TryGetPaletteIndex(x, y, out int index);
        if (hasIndex && image.IsInvalidIndex(x, y))
        {
            var palette = image.Palette!;
            text.AppendLine(CultureInfo.InvariantCulture, $"Palette index {index}: outside the colour map ({palette.FirstIndex}–{palette.LastIndex}), so it has no colour.");
            return text.ToString();
        }

        uint p = image.GetPixel(x, y);
        uint r = (p >> 16) & 0xFF, g = (p >> 8) & 0xFF, b = p & 0xFF, a = p >> 24;
        var header = image.Header;
        if (hasIndex)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Palette index {index} ({image.Palette!.EntryBits}-bit entry)");
        }

        int sourceBits = hasIndex ? image.Palette!.EntryBits : header.PixelDepth;
        bool fiveBit = header.Kind != TgaImageKind.Grayscale && sourceBits is 15 or 16;
        if (fiveBit)
        {
            uint word = (r >> 3) << 10 | (g >> 3) << 5 | (b >> 3);
            if (sourceBits == 16 && a != 0)
            {
                word |= 0x8000;
            }

            text.AppendLine(CultureInfo.InvariantCulture, $"Stored {(hasIndex ? "entry" : "word")} 0x{word:X4}: 5-bit R {r >> 3}, G {g >> 3}, B {b >> 3}{(sourceBits == 16 ? $", attribute bit {(a != 0 ? 1 : 0)}" : "")}");
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"R {r}  G {g}  B {b}  (#{r:X2}{g:X2}{b:X2})");
        text.AppendLine(CultureInfo.InvariantCulture, $"Normalised R {r / 255.0:0.000}  G {g / 255.0:0.000}  B {b / 255.0:0.000}");

        switch (image.Alpha.Storage)
        {
            case TgaAlphaStorage.None:
                text.AppendLine("A 255 (not stored; this format has no alpha)");
                break;
            case TgaAlphaStorage.OneBit:
                text.AppendLine(CultureInfo.InvariantCulture, $"A {a} (attribute bit {(a != 0 ? 1 : 0)})");
                break;
            default:
                text.AppendLine(CultureInfo.InvariantCulture, $"A {a}  (normalised {a / 255.0:0.000})");
                break;
        }

        if (decision.Interpretation == AlphaInterpretation.Premultiplied)
        {
            if (!DisplayRenderer.IsValidPremultiplied(p))
            {
                text.AppendLine("Invalid premultiplied data: a colour channel exceeds alpha. For display only, colour is clamped to alpha.");
            }
            else if (a > 0)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"Unpremultiplied (derived, not stored): R {Straight(r, a)}  G {Straight(g, a)}  B {Straight(b, a)}");
            }
        }

        return text.ToString();
    }

    private static uint Straight(uint channel, uint alpha) => Math.Min(255u, (channel * 255 + alpha / 2) / alpha);
}
