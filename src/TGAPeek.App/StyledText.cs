using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace TGAPeek.App;

/// <summary>
/// Read-only text boxes for the information panel. The text is exactly what the Core reports
/// produce; only colour is added: channel values in their channel colours, and in the image report,
/// section titles and field names.
/// </summary>
internal static partial class StyledText
{
    private const int WM_SETREDRAW = 0x000B;

    public static RichTextBox Create(string name) => new()
    {
        ReadOnly = true,
        Multiline = true,
        ScrollBars = RichTextBoxScrollBars.Both,
        WordWrap = false,
        DetectUrls = false,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Panel,
        ForeColor = Theme.Text,
        Font = Theme.MonoFont,
        AccessibleName = name,
    };

    /// <summary>The pixel inspector: R, G, B and A values, and the #RRGGBB pairs, in their channel colours.</summary>
    public static void SetInspector(RichTextBox box, string text) => Set(box, text, shown =>
    {
        foreach (Match match in Theme.ChannelValues().Matches(shown))
        {
            Colour(box, match.Index, match.Length, Theme.ChannelText(match.Value[0]));
        }

        foreach (Match match in Theme.HexColour().Matches(shown))
        {
            Colour(box, match.Groups[1].Index, 2, Theme.RedText);
            Colour(box, match.Groups[2].Index, 2, Theme.GreenText);
            Colour(box, match.Groups[3].Index, 2, Theme.BlueText);
        }
    });

    /// <summary>The image report: section titles stand out and field names recede.</summary>
    public static void SetReport(RichTextBox box, string text) => Set(box, text, shown =>
    {
        foreach (Match match in SectionTitle().Matches(shown))
        {
            Colour(box, match.Index, match.Length, Theme.Heading);
        }

        foreach (Match match in FieldName().Matches(shown))
        {
            Colour(box, match.Index, match.Length, Theme.TextMuted);
        }
    });

    private static void Set(RichTextBox box, string text, Action<string> style)
    {
        bool redraw = box.IsHandleCreated;
        if (redraw)
        {
            SendMessage(box.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        }

        try
        {
            box.Text = text;
            style(box.Text); // positions in the control's own text, whose line breaks may differ
            box.Select(0, 0);
        }
        finally
        {
            if (redraw)
            {
                SendMessage(box.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                box.Invalidate();
            }
        }
    }

    private static void Colour(RichTextBox box, int start, int length, Color color)
    {
        box.Select(start, length);
        box.SelectionColor = color;
    }

    /// <summary>A report section title: a whole line in capitals, such as "FILE" or "TGA 2.0 METADATA".</summary>
    [GeneratedRegex(@"^[A-Z][A-Z0-9 .]*$", RegexOptions.Multiline)]
    private static partial Regex SectionTitle();

    /// <summary>A field name: indented by two spaces and followed by a gap of two or more spaces.</summary>
    [GeneratedRegex(@"^  \S.*?(?=[ ]{2,}\S)", RegexOptions.Multiline)]
    private static partial Regex FieldName();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}
