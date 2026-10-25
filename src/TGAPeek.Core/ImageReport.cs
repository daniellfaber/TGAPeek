using System.Globalization;
using System.Text;

namespace TGAPeek.Core;

/// <summary>Builds the plain-text information shown in the viewer's information panel.</summary>
public static class ImageReport
{
    public static string Build(TgaImage image, string? path, AlphaDecision decision)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(decision);
        var h = image.Header;
        var m = image.Metadata;
        var t = new StringBuilder();

        Section(t, "FILE");
        if (path is not null)
        {
            Line(t, "Name", Path.GetFileName(path));
            Line(t, "Folder", Path.GetDirectoryName(path) ?? "");
        }

        if (m.FileLength >= 0)
        {
            Line(t, "Size", $"{m.FileLength:N0} bytes");
        }

        Line(t, "Version", m.HasFooter ? "TGA 2.0 (has footer)" : "TGA 1.0 (no TGA 2.0 footer)");

        Section(t, "IMAGE");
        Line(t, "Dimensions", $"{h.Width} × {h.Height} pixels");
        Line(t, "Format", image.FormatDescription);
        Line(t, "Image type", $"{h.ImageType} ({h.ImageTypeName}, {h.CompressionName.ToLowerInvariant()})");
        Line(t, "Pixel depth", $"{h.PixelDepth} bits");
        Line(t, "Attribute bits", h.AttributeBits.ToString(CultureInfo.InvariantCulture));
        Line(t, "First pixel", $"{h.OriginName} (descriptor 0x{h.Descriptor:X2})");
        Line(t, "Interleaving", h.Interleave switch
        {
            TgaInterleave.TwoWay => "two-way",
            TgaInterleave.FourWay => "four-way",
            _ => "none",
        });
        Line(t, "Screen origin", $"x {h.XOrigin}, y {h.YOrigin} (informational)");
        if (image.Palette is { } palette)
        {
            Line(t, "Colour map", $"{palette.Length} entries ({palette.FirstIndex}–{palette.LastIndex}), {palette.EntryBits}-bit");
        }
        else if (m.HasUnusedColorMap)
        {
            Line(t, "Colour map", $"{h.ColorMapLength} entries present but unused (skipped)");
        }

        Line(t, "Image ID", m.ImageIdText);
        Line(t, "Pixel data", $"bytes {m.PixelDataOffset:N0}–{m.PixelDataEnd:N0}");
        if (m.FileLength >= 0 && !m.HasFooter && m.FileLength > m.PixelDataEnd)
        {
            Line(t, "After pixels", $"{m.FileLength - m.PixelDataEnd:N0} bytes of additional data (ignored)");
        }

        Section(t, "STATUS");
        if (image.IsPartial)
        {
            Line(t, "Decoded", $"{image.DecodedPixelCount:N0} of {image.PixelCount:N0} pixels (partial: file truncated)");
        }
        else
        {
            Line(t, "Decoded", $"all {image.PixelCount:N0} pixels");
        }

        if (image.InvalidIndexPixels is { } invalid)
        {
            Line(t, "Invalid indices", $"{invalid.CountSet():N0} pixels (marked)");
        }

        Section(t, "ALPHA");
        var a = image.Alpha;
        Line(t, "Stored", a.Storage switch
        {
            TgaAlphaStorage.EightBit => "8 bits per pixel",
            TgaAlphaStorage.OneBit => "1 attribute bit per pixel",
            _ => "none",
        });
        Line(t, "Header bits", a.DeclaredAttributeBits.ToString(CultureInfo.InvariantCulture));
        Line(t, "Attributes type", a.AttributesType switch
        {
            null => "not specified",
            0 => "0 (no alpha data)",
            1 => "1 (undefined, can be ignored)",
            2 => "2 (undefined, should be retained)",
            3 => "3 (useful alpha)",
            _ => "4 (premultiplied alpha)",
        });
        if (a.HasStoredAlpha)
        {
            Line(t, "Values", a.AllZero ? "all 0" : a.AllOpaque ? "all 255" : "mixed");
        }

        Line(t, "Display", decision.Summary);
        Wrap(t, decision.Explanation);
        if (decision.Interpretation == AlphaInterpretation.Premultiplied)
        {
            long bad = image.CountInvalidPremultipliedPixels();
            Line(t, "Invalid premult.", bad == 0 ? "none" : $"{bad:N0} pixels have colour above alpha (clamped for display)");
        }

        if (m.HasFooter)
        {
            AppendTga2(t, image);
        }

        Section(t, "WARNINGS");
        if (image.Warnings.Count == 0)
        {
            t.AppendLine("  none");
        }

        foreach (var warning in image.Warnings)
        {
            Wrap(t, (warning.IsNote ? "Note: " : "Warning: ") + warning.Message);
        }

        return t.ToString();
    }

    private static void AppendTga2(StringBuilder t, TgaImage image)
    {
        var m = image.Metadata;
        Section(t, "TGA 2.0 METADATA");
        Status(t, "Extension area", m.Extension);
        if (m.Extension.Value is { } e)
        {
            Optional(t, "Author", e.AuthorName);
            foreach (string comment in e.AuthorComments)
            {
                Optional(t, "Comment", comment);
            }

            Optional(t, "Date/time", e.Timestamp);
            Optional(t, "Job", e.JobName);
            Optional(t, "Job time", e.JobTime);
            Optional(t, "Software", e.SoftwareId);
            Optional(t, "Version", e.SoftwareVersion);
            Line(t, "Key colour", $"A {e.KeyColor >> 24} R {(e.KeyColor >> 16) & 0xFF} G {(e.KeyColor >> 8) & 0xFF} B {e.KeyColor & 0xFF}");
            Line(t, "Pixel aspect", e.PixelAspectRatio is { } ratio
                ? $"{e.PixelAspectNumerator}:{e.PixelAspectDenominator} ({ratio:0.###}); not applied, pixels are shown square"
                : "not specified");
            Line(t, "Gamma", e.Gamma is { } gamma
                ? $"{gamma:0.###} ({e.GammaNumerator}/{e.GammaDenominator}); recorded only, not applied"
                : "not specified");
        }

        Status(t, "Developer dir.", m.DeveloperDirectory);
        if (m.DeveloperDirectory.Value is { } directory)
        {
            for (int i = 0; i < directory.Entries.Count && i < 32; i++)
            {
                var entry = directory.Entries[i];
                string preview = i < directory.Previews.Count && directory.Previews[i] is { } bytes ? "  " + TgaText.Describe(bytes) : "";
                Line(t, $"  Tag {entry.Tag}", $"offset {entry.Offset}, {entry.Size} bytes{(entry.IsValid ? "" : " (outside the file, ignored)")}{preview}");
            }

            if (directory.Entries.Count > 32)
            {
                t.AppendLine(CultureInfo.InvariantCulture, $"  … and {directory.Entries.Count - 32} more entries");
            }
        }

        Status(t, "Colour correct.", m.ColorCorrection);
        if (m.ColorCorrection.Value is { } table)
        {
            Line(t, "", "256 entries (A, R, G, B); recorded only, never applied");
            for (int i = 0; i < 256; i += 85)
            {
                Line(t, $"  Entry {i}", $"A {table.Values[i * 4]} R {table.Values[i * 4 + 1]} G {table.Values[i * 4 + 2]} B {table.Values[i * 4 + 3]}");
            }
        }

        Status(t, "Postage stamp", m.PostageStamp);
        if (m.PostageStamp.Value is { } stamp)
        {
            Line(t, "", $"{stamp.Width} × {stamp.Height} pixels{(stamp.InvalidIndexCount > 0 ? $", {stamp.InvalidIndexCount} invalid indices" : "")}");
        }

        Status(t, "Scan-line table", m.ScanLineTable);
        if (m.ScanLineTable.Value is { } scan)
        {
            Line(t, "", scan.MismatchCount == 0
                ? $"{scan.Offsets.Count} entries, consistent with the pixel data"
                : $"{scan.Offsets.Count} entries, {scan.MismatchCount} inconsistent (not used for decoding)");
        }
    }

    private static void Status<T>(StringBuilder t, string name, TgaStructure<T> structure)
        where T : class
    {
        string text = structure.Status switch
        {
            TgaStructureStatus.Absent => "not present",
            TgaStructureStatus.Valid => $"present at offset {structure.Offset}",
            _ => $"invalid at offset {structure.Offset}, ignored because {structure.Problem}",
        };
        Line(t, name, text);
    }

    private static void Section(StringBuilder t, string title)
    {
        if (t.Length > 0)
        {
            t.AppendLine();
        }

        t.AppendLine(title);
    }

    private static void Line(StringBuilder t, string label, string value) =>
        t.AppendLine(CultureInfo.InvariantCulture, $"  {label,-16} {value}");

    private static void Optional(StringBuilder t, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Line(t, label, value);
        }
    }

    private static void Wrap(StringBuilder t, string text)
    {
        const int width = 72;
        var line = new StringBuilder("  ");
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length + word.Length + 1 > width && line.Length > 2)
            {
                t.AppendLine(line.ToString());
                line.Clear().Append("  ");
            }

            if (line.Length > 2)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 2)
        {
            t.AppendLine(line.ToString());
        }
    }
}
