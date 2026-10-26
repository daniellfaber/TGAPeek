namespace TGAPeek.App;

/// <summary>A panel section title in small capitals, with an optional hint and a divider below.</summary>
internal sealed class SectionHeader : Control
{
    private readonly string? _hint;

    public SectionHeader(string title, string? hint = null)
    {
        _hint = hint;
        Text = title;
        AccessibleName = hint is null ? title : $"{title}: {hint}";
        AccessibleRole = AccessibleRole.StaticText;
        Dock = DockStyle.Top;
        BackColor = Theme.Panel;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        int left = LogicalToDeviceUnits(12);
        var area = new Rectangle(left, 0, Width - left, Height - LogicalToDeviceUnits(2));
        string title = Text.ToUpperInvariant();
        TextRenderer.DrawText(g, title, Theme.HeaderFont, area, Theme.TextMuted, flags);
        if (_hint is not null)
        {
            int width = TextRenderer.MeasureText(g, title, Theme.HeaderFont, area.Size, flags).Width + LogicalToDeviceUnits(10);
            var hintArea = Rectangle.FromLTRB(area.Left + width, area.Top, area.Right - LogicalToDeviceUnits(8), area.Bottom);
            TextRenderer.DrawText(g, _hint, Theme.UiFont, hintArea, Theme.TextFaint, flags | TextFormatFlags.EndEllipsis);
        }

        using var pen = new Pen(Theme.Divider);
        g.DrawLine(pen, left, Height - 1, Width - LogicalToDeviceUnits(12), Height - 1);
    }
}
