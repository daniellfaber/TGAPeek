using System.Drawing.Drawing2D;
using TGAPeek.Core;

namespace TGAPeek.App;

/// <summary>Marks the status-bar label whose channel values are drawn in their channel colours.</summary>
internal sealed class PixelReadout
{
    public static readonly PixelReadout Tag = new();
}

/// <summary>
/// Paints menus, the toolbar and the status bar in the dark theme (docs/DESIGN.md). It only changes
/// how items look; every item keeps its text, shortcut, tooltip and behaviour. Channel buttons
/// (Tag = <see cref="ChannelView"/>) form a colour-coded segmented control.
/// </summary>
internal sealed class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer()
        : base(new DarkColors())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        Color back = e.ToolStrip switch
        {
            ToolStripDropDown => Theme.Control,
            MenuStrip or StatusStrip => Theme.Chrome,
            _ => Theme.Toolbar,
        };
        using var brush = new SolidBrush(back);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var size = e.ToolStrip.Size;
        switch (e.ToolStrip)
        {
            case ToolStripDropDown:
                using (var pen = new Pen(Theme.Border))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, size.Width - 1, size.Height - 1);
                }

                break;
            case StatusStrip:
                using (var pen = new Pen(Theme.Divider))
                {
                    e.Graphics.DrawLine(pen, 0, 0, size.Width, 0);
                }

                break;
            case MenuStrip:
                break;
            default:
                using (var pen = new Pen(Theme.Divider))
                {
                    e.Graphics.DrawLine(pen, 0, size.Height - 1, size.Width, size.Height - 1);
                }

                break;
        }
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Theme.Control);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        var bounds = new Rectangle(Point.Empty, item.Size);
        if (item.Owner is MenuStrip)
        {
            Color? fill = item.Pressed ? Theme.Control : item.Selected ? Theme.Hover : null;
            if (fill is { } color)
            {
                Theme.FillRounded(e.Graphics, Inset(bounds, 1, 2, item), Scale(4, item), color);
            }

            return;
        }

        if (item.Selected && item.Enabled)
        {
            Theme.FillRounded(e.Graphics, Inset(bounds, 4, 1, item), Scale(4, item), Theme.Hover);
        }
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is not ToolStripButton button)
        {
            base.OnRenderButtonBackground(e);
            return;
        }

        if (button.Tag is ChannelView channel)
        {
            DrawChannelButton(e.Graphics, button, channel);
            return;
        }

        var area = Inset(new Rectangle(Point.Empty, button.Size), 1, 3, button);
        Color? fill = button.Pressed ? Theme.Pressed
            : button.Checked ? (button.Selected ? Theme.AccentFillHover : Theme.AccentFill)
            : button.Selected ? Theme.Hover
            : null;
        if (fill is { } color)
        {
            Theme.FillRounded(e.Graphics, area, Scale(4, button), color);
        }

        if (button.Checked)
        {
            // An accent underline marks toggles that are on, so colour is not the only cue.
            int height = Scale(2, button);
            int inset = Scale(8, button);
            using var brush = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(brush, area.Left + inset, area.Bottom - height, Math.Max(1, area.Width - 2 * inset), height);
        }
    }

    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        Color? fill = item.Pressed ? Theme.Pressed : item.Selected ? Theme.Hover : null;
        if (fill is { } color)
        {
            Theme.FillRounded(e.Graphics, Inset(new Rectangle(Point.Empty, item.Size), 1, 3, item), Scale(4, item), color);
        }
    }

    protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        var bounds = new Rectangle(Point.Empty, item.Size);
        if (item.Pressed || item.Selected)
        {
            Theme.FillRounded(e.Graphics, Inset(bounds, 1, 3, item), Scale(4, item), item.Pressed ? Theme.Pressed : Theme.Hover);
        }

        Theme.DrawGlyph(e.Graphics, '', bounds, Scale(10, item), Theme.TextMuted);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var item = e.Item;
        using var pen = new Pen(e.ToolStrip is ToolStripDropDown ? Theme.Border : Theme.Divider);
        if (e.Vertical)
        {
            int x = item.Width / 2;
            int inset = Scale(item.Owner is StatusStrip ? 5 : 7, item);
            e.Graphics.DrawLine(pen, x, inset, x, item.Height - inset);
        }
        else
        {
            int y = item.Height / 2;
            int inset = Scale(8, item);
            e.Graphics.DrawLine(pen, inset, y, item.Width - inset, y);
        }
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var area = e.ImageRectangle;
        Theme.FillRounded(e.Graphics, Rectangle.Inflate(area, -1, -1), Scale(3, e.Item), Theme.AccentFill);
        Theme.DrawGlyph(e.Graphics, Theme.GlyphCheckMark, area, Scale(11, e.Item), Theme.Heading);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled == false ? Theme.TextFaint : Theme.TextMuted;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderStatusStripSizingGrip(ToolStripRenderEventArgs e)
    {
        // Six small dots in the corner, like the system grip but in the theme's faint colour.
        var bounds = e.AffectedBounds;
        int dot = Scale(2, e.ToolStrip);
        int gap = Scale(4, e.ToolStrip);
        using var brush = new SolidBrush(Theme.TextFaint);
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column <= row; column++)
            {
                int x = bounds.Right - gap * (column + 1) - Scale(2, e.ToolStrip);
                int y = bounds.Bottom - gap * (3 - row) - Scale(1, e.ToolStrip);
                e.Graphics.FillRectangle(brush, x, y, dot, dot);
            }
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var item = e.Item;
        if (item is ToolStripStatusLabel { IsLink: false } && item.Owner is StatusStrip strip && strip.Items.IndexOf(item) > 0 && !string.IsNullOrEmpty(e.Text))
        {
            // Status fields after the first get a divider on their left edge while they show text.
            int inset = Scale(4, item);
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, inset, 0, item.Height - inset);
        }

        if (ReferenceEquals(item.Tag, PixelReadout.Tag) && !string.IsNullOrEmpty(e.Text))
        {
            DrawPixelReadout(e);
            return;
        }

        if (!item.Enabled)
        {
            e.TextColor = Theme.TextFaint;
        }
        else if (item.Tag is ChannelView channel && item is ToolStripButton button)
        {
            e.TextColor = button.Checked ? Theme.TextOnFill : Theme.Style(channel).Text;
        }
        else if (item.Owner is StatusStrip)
        {
            e.TextColor = item is ToolStripStatusLabel { IsLink: true } ? Theme.Warning : item.ForeColor;
        }
        else if (item is ToolStripMenuItem menuItem && item.Owner is ToolStripDropDown && e.Text != menuItem.Text)
        {
            e.TextColor = Theme.TextMuted; // the shortcut column
        }
        else
        {
            e.TextColor = Theme.Text;
        }

        base.OnRenderItemText(e);
    }

    /// <summary>A rounded segment of the channel control, with a colour stripe naming its channel.</summary>
    private static void DrawChannelButton(Graphics g, ToolStripButton button, ChannelView channel)
    {
        var style = Theme.Style(channel);
        var bounds = new Rectangle(Point.Empty, button.Size);
        var segment = new Rectangle(0, Scale(3, button), bounds.Width, bounds.Height - Scale(6, button));
        bool first = channel == ChannelView.Rgba;
        bool last = channel == ChannelView.Alpha;
        int radius = Scale(4, button);
        Theme.FillRounded(g, segment, radius, Theme.Control, first, last);

        if (button.Checked || button.Pressed || button.Selected)
        {
            var inner = Rectangle.Inflate(segment, -Scale(2, button), -Scale(2, button));
            Color fill = button.Checked ? style.Fill : button.Pressed ? Theme.Pressed : Theme.Hover;
            Theme.FillRounded(g, inner, Scale(3, button), fill);
        }

        // The stripe names the channel by colour: one segment per channel it shows.
        int height = Scale(2, button);
        int inset = Scale(7, button);
        int width = Math.Max(style.Stripes.Length, segment.Width - 2 * inset);
        int y = segment.Bottom - Scale(4, button) - height;
        for (int i = 0; i < style.Stripes.Length; i++)
        {
            int left = segment.Left + inset + width * i / style.Stripes.Length;
            int right = segment.Left + inset + width * (i + 1) / style.Stripes.Length;
            using var brush = new SolidBrush(style.Stripes[i]);
            g.FillRectangle(brush, left, y, right - left, height);
        }

        if (!last)
        {
            using var pen = new Pen(Theme.Toolbar);
            g.DrawLine(pen, bounds.Right - 1, segment.Top, bounds.Right - 1, segment.Bottom);
        }
    }

    /// <summary>The status-bar pixel summary, with each channel value in its channel's colour.</summary>
    private static void DrawPixelReadout(ToolStripItemTextRenderEventArgs e)
    {
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        var g = e.Graphics;
        var area = e.TextRectangle;
        int x = area.Left;
        int last = 0;

        void Draw(string text, Color color)
        {
            if (text.Length == 0 || x >= area.Right)
            {
                return;
            }

            // A proposed width of short.MaxValue: int.MaxValue overflows inside GDI and measures as zero.
            int width = TextRenderer.MeasureText(g, text, e.TextFont, new Size(short.MaxValue, area.Height), flags).Width;
            TextRenderer.DrawText(g, text, e.TextFont, new Rectangle(x, area.Top, Math.Min(width, area.Right - x), area.Height), color, flags);
            x += width;
        }

        foreach (System.Text.RegularExpressions.Match match in Theme.ChannelValues().Matches(e.Text!))
        {
            Draw(e.Text![last..match.Index], Theme.Text);
            Draw(match.Value, Theme.ChannelText(match.Value[0]));
            last = match.Index + match.Length;
        }

        Draw(e.Text![last..], Theme.Text);
    }

    private static Rectangle Inset(Rectangle r, int horizontal, int vertical, ToolStripItem item) =>
        Rectangle.FromLTRB(r.Left + Scale(horizontal, item), r.Top + Scale(vertical, item), r.Right - Scale(horizontal, item), r.Bottom - Scale(vertical, item));

    private static int Scale(int logical, ToolStripItem item) => item.Owner is { } owner ? owner.LogicalToDeviceUnits(logical) : logical;

    private static int Scale(int logical, System.Windows.Forms.Control control) => control.LogicalToDeviceUnits(logical);

    /// <summary>Colours for anything the base renderer still paints, so no light default shows through.</summary>
    private sealed class DarkColors : ProfessionalColorTable
    {
        public DarkColors()
        {
            UseSystemColors = false;
        }

        public override Color ToolStripGradientBegin => Theme.Toolbar;
        public override Color ToolStripGradientMiddle => Theme.Toolbar;
        public override Color ToolStripGradientEnd => Theme.Toolbar;
        public override Color ToolStripBorder => Theme.Divider;
        public override Color ToolStripDropDownBackground => Theme.Control;
        public override Color ToolStripContentPanelGradientBegin => Theme.Chrome;
        public override Color ToolStripContentPanelGradientEnd => Theme.Chrome;
        public override Color ToolStripPanelGradientBegin => Theme.Chrome;
        public override Color ToolStripPanelGradientEnd => Theme.Chrome;
        public override Color MenuStripGradientBegin => Theme.Chrome;
        public override Color MenuStripGradientEnd => Theme.Chrome;
        public override Color StatusStripGradientBegin => Theme.Chrome;
        public override Color StatusStripGradientEnd => Theme.Chrome;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.Hover;
        public override Color MenuItemSelected => Theme.Hover;
        public override Color MenuItemSelectedGradientBegin => Theme.Hover;
        public override Color MenuItemSelectedGradientEnd => Theme.Hover;
        public override Color MenuItemPressedGradientBegin => Theme.Control;
        public override Color MenuItemPressedGradientMiddle => Theme.Control;
        public override Color MenuItemPressedGradientEnd => Theme.Control;
        public override Color ImageMarginGradientBegin => Theme.Control;
        public override Color ImageMarginGradientMiddle => Theme.Control;
        public override Color ImageMarginGradientEnd => Theme.Control;
        public override Color ImageMarginRevealedGradientBegin => Theme.Control;
        public override Color ImageMarginRevealedGradientMiddle => Theme.Control;
        public override Color ImageMarginRevealedGradientEnd => Theme.Control;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
        public override Color ButtonSelectedHighlight => Theme.Hover;
        public override Color ButtonSelectedHighlightBorder => Theme.Hover;
        public override Color ButtonSelectedBorder => Theme.Hover;
        public override Color ButtonSelectedGradientBegin => Theme.Hover;
        public override Color ButtonSelectedGradientMiddle => Theme.Hover;
        public override Color ButtonSelectedGradientEnd => Theme.Hover;
        public override Color ButtonPressedHighlight => Theme.Pressed;
        public override Color ButtonPressedHighlightBorder => Theme.Pressed;
        public override Color ButtonPressedBorder => Theme.Pressed;
        public override Color ButtonPressedGradientBegin => Theme.Pressed;
        public override Color ButtonPressedGradientMiddle => Theme.Pressed;
        public override Color ButtonPressedGradientEnd => Theme.Pressed;
        public override Color ButtonCheckedHighlight => Theme.AccentFill;
        public override Color ButtonCheckedHighlightBorder => Theme.AccentFill;
        public override Color ButtonCheckedGradientBegin => Theme.AccentFill;
        public override Color ButtonCheckedGradientMiddle => Theme.AccentFill;
        public override Color ButtonCheckedGradientEnd => Theme.AccentFill;
        public override Color CheckBackground => Theme.AccentFill;
        public override Color CheckSelectedBackground => Theme.AccentFillHover;
        public override Color CheckPressedBackground => Theme.AccentFillHover;
        public override Color OverflowButtonGradientBegin => Theme.Toolbar;
        public override Color OverflowButtonGradientMiddle => Theme.Toolbar;
        public override Color OverflowButtonGradientEnd => Theme.Toolbar;
        public override Color GripDark => Theme.Border;
        public override Color GripLight => Theme.Toolbar;
        public override Color RaftingContainerGradientBegin => Theme.Chrome;
        public override Color RaftingContainerGradientEnd => Theme.Chrome;
    }
}
