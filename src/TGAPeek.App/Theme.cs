using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using TGAPeek.Core;

namespace TGAPeek.App;

/// <summary>How one channel button and its values are coloured.</summary>
internal sealed record ChannelStyle(Color Text, Color Fill, Color[] Stripes);

/// <summary>
/// The dark design system described in docs/DESIGN.md: colour tokens, fonts, icons and the native
/// calls that darken the title bar and scroll bars. Chrome colours are never pure greys or pure
/// primaries, so they can't be confused with image pixels ("Exactness rule" in DESIGN.md).
/// </summary>
internal static partial class Theme
{
    // Surfaces, darkest first.
    public static readonly Color Canvas = Rgb(0x16, 0x17, 0x1A);
    public static readonly Color Chrome = Rgb(0x1C, 0x1D, 0x21);
    public static readonly Color Panel = Rgb(0x1F, 0x20, 0x24);
    public static readonly Color Toolbar = Rgb(0x23, 0x24, 0x28);
    public static readonly Color Control = Rgb(0x2C, 0x2E, 0x33);
    public static readonly Color Hover = Rgb(0x37, 0x39, 0x3F);
    public static readonly Color Pressed = Rgb(0x42, 0x45, 0x4C);
    public static readonly Color Divider = Rgb(0x2A, 0x2C, 0x31);
    public static readonly Color Border = Rgb(0x35, 0x37, 0x3D);

    // Text.
    public static readonly Color Text = Rgb(0xE4, 0xE5, 0xE9);
    public static readonly Color TextMuted = Rgb(0x9B, 0x9E, 0xA8);
    public static readonly Color TextFaint = Rgb(0x6A, 0x6D, 0x76);
    public static readonly Color TextOnFill = Rgb(0xF6, 0xF7, 0xF9);
    public static readonly Color Heading = Rgb(0x8C, 0xB2, 0xF4);

    // Accent and status.
    public static readonly Color Accent = Rgb(0x3D, 0x86, 0xF2);
    public static readonly Color AccentFill = Rgb(0x26, 0x3B, 0x5C);
    public static readonly Color AccentFillHover = Rgb(0x2D, 0x46, 0x6D);
    public static readonly Color Warning = Rgb(0xF0, 0xB2, 0x4A);

    // Channel colours: text on dark surfaces, the checked fill, and the identifying stripe.
    public static readonly Color RedText = Rgb(0xF2, 0x77, 0x7C);
    public static readonly Color GreenText = Rgb(0x6F, 0xD0, 0x8A);
    public static readonly Color BlueText = Rgb(0x6E, 0xA8, 0xF7);
    public static readonly Color AlphaText = Rgb(0xC7, 0xCA, 0xD2);
    private static readonly Color RedStripe = Rgb(0xE5, 0x48, 0x4F);
    private static readonly Color GreenStripe = Rgb(0x43, 0xB5, 0x62);
    private static readonly Color BlueStripe = Rgb(0x3F, 0x86, 0xEC);
    private static readonly Color AlphaStripe = Rgb(0xA7, 0xAB, 0xB5);

    /// <summary>Transparency checkerboard behind the image (display only), dark like the chrome.</summary>
    public const uint CheckerLight = 0xFF5A5A5A;
    public const uint CheckerDark = 0xFF424242;

    // Icon glyphs from Segoe Fluent Icons (Windows 11) or Segoe MDL2 Assets (Windows 10).
    public const char GlyphOpen = '';
    public const char GlyphFit = '';
    public const char GlyphActualSize = '';
    public const char GlyphZoomOut = '';
    public const char GlyphZoomIn = '';
    public const char GlyphSmooth = '';
    public const char GlyphChecker = '';
    public const char GlyphInfo = '';
    public const char GlyphImage = '';
    public const char GlyphWarning = '';
    public const char GlyphCheckMark = '';

    public static readonly Font UiFont = new("Segoe UI", 9F);
    public static readonly Font UiSemibold = new("Segoe UI Semibold", 9F);
    public static readonly Font HeaderFont = new("Segoe UI Semibold", 8.25F);
    public static readonly Font PlaceholderFont = new("Segoe UI", 10.5F);
    public static readonly Font MonoFont = new(Installed("Cascadia Mono") ? "Cascadia Mono" : "Consolas", 9F);
    private static readonly string IconFamily = Installed("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    public static ChannelStyle Style(ChannelView channel) => channel switch
    {
        ChannelView.Red => new(RedText, Rgb(0xB3, 0x3B, 0x42), [RedStripe]),
        ChannelView.Green => new(GreenText, Rgb(0x2E, 0x7D, 0x46), [GreenStripe]),
        ChannelView.Blue => new(BlueText, Rgb(0x2E, 0x62, 0xB3), [BlueStripe]),
        ChannelView.Alpha => new(AlphaText, Rgb(0x5E, 0x61, 0x6A), [AlphaStripe]),
        ChannelView.Rgb => new(Text, Rgb(0x46, 0x4A, 0x55), [RedStripe, GreenStripe, BlueStripe]),
        _ => new(Text, Rgb(0x46, 0x4A, 0x55), [RedStripe, GreenStripe, BlueStripe, AlphaStripe]),
    };

    /// <summary>The text colour for a channel letter (R, G, B or A).</summary>
    public static Color ChannelText(char channel) => channel switch
    {
        'R' => RedText,
        'G' => GreenText,
        'B' => BlueText,
        _ => AlphaText,
    };

    /// <summary>Channel values in inspector text, such as "R 255", "G 0.502" or "A —".</summary>
    [GeneratedRegex(@"(?<![A-Za-z])([RGBA]) (\d+(?:\.\d+)?|—)")]
    public static partial Regex ChannelValues();

    /// <summary>A colour written as #RRGGBB, so each pair can take its channel's colour.</summary>
    [GeneratedRegex(@"#([0-9A-F]{2})([0-9A-F]{2})([0-9A-F]{2})\b")]
    public static partial Regex HexColour();

    /// <summary>An icon glyph drawn into a transparent bitmap of the given pixel size.</summary>
    public static Bitmap Icon(char glyph, int size, Color color)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font(IconFamily, size * 0.8F, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(glyph.ToString(), font, brush, new RectangleF(0, 0, size, size), format);
        return bitmap;
    }

    /// <summary>Draws an icon glyph centred in a rectangle.</summary>
    public static void DrawGlyph(Graphics g, char glyph, Rectangle area, float pixelSize, Color color)
    {
        using var font = new Font(IconFamily, pixelSize, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, glyph.ToString(), font, area, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }

    public static GraphicsPath Rounded(Rectangle r, int radius, bool left = true, bool right = true)
    {
        var path = new GraphicsPath();
        int d = Math.Max(1, radius * 2);
        if (left)
        {
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
        }
        else
        {
            path.AddLine(r.Left, r.Bottom, r.Left, r.Top);
        }

        if (right)
        {
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        }
        else
        {
            path.AddLine(r.Right, r.Top, r.Right, r.Bottom);
        }

        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, Rectangle r, int radius, Color color, bool left = true, bool right = true)
    {
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(r, radius, left, right);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
        g.SmoothingMode = smoothing;
    }

    /// <summary>A dark title bar and window border, matching the chrome (no effect before Windows 10 20H1).</summary>
    public static void ApplyDarkFrame(IntPtr hwnd)
    {
        int on = 1;
        _ = DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
        int caption = ColorRef(Chrome);
        int text = ColorRef(Text);
        int border = ColorRef(Border);
        _ = DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int)); // DWMWA_CAPTION_COLOR (Windows 11)
        _ = DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int)); // DWMWA_TEXT_COLOR (Windows 11)
        _ = DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int)); // DWMWA_BORDER_COLOR (Windows 11)
    }

    /// <summary>Dark scroll bars for a text control, now or when its window is created.</summary>
    public static void UseDarkScrollBars(System.Windows.Forms.Control control)
    {
        if (control.IsHandleCreated)
        {
            _ = SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        }

        control.HandleCreated += (_, _) => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
    }

    private static Color Rgb(int r, int g, int b) => Color.FromArgb(r, g, b);

    private static int ColorRef(Color c) => c.R | c.G << 8 | c.B << 16;

    private static bool Installed(string family)
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? idList);
}
