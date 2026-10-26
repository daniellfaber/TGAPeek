using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TGAPeek.Core;

namespace TGAPeek.App;

internal sealed class PixelEventArgs(Point? pixel) : EventArgs
{
    /// <summary>The image pixel under the mouse, or null when outside the image.</summary>
    public Point? Pixel { get; } = pixel;
}

/// <summary>
/// Shows a <see cref="TgaImage"/>. <see cref="DisplayRenderer"/> composites the visible area into an
/// opaque buffer exactly the size of the control, which is drawn 1:1, so Windows never scales,
/// blends or colour-converts image pixels.
/// </summary>
internal sealed class ImageCanvas : Control
{
    private const double ZoomStep = 1.25;

    private readonly System.Windows.Forms.Timer _settleTimer = new() { Interval = 150 };
    private TgaImage? _image;
    private AlphaInterpretation _alpha;
    private DisplayOptions _options = new();
    private ViewTransform _view = new(1, 0, 0);
    private bool _fitMode = true;
    private uint[]? _buffer;
    private Bitmap? _bitmap;
    private int _bufferWidth;
    private int _bufferHeight;
    private bool _dirty = true;
    private bool _interacting;
    private bool _dragging;
    private bool _dragMoved;
    private Point _dragStart;
    private ViewTransform _dragView;
    private string? _banner;
    private string? _overlay;

    public ImageCanvas()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.Selectable | ControlStyles.ResizeRedraw,
            true);
        TabStop = true;
        AccessibleName = "Image view";
        AccessibleRole = AccessibleRole.Graphic;
        BackColor = Theme.Canvas;
        _settleTimer.Tick += (_, _) =>
        {
            // Smooth scaling is skipped while zooming or panning, then applied once things settle.
            _settleTimer.Stop();
            _interacting = false;
            _dirty = true;
            Invalidate();
        };
    }

    public event EventHandler<PixelEventArgs>? PixelHovered;

    public event EventHandler<PixelEventArgs>? PixelClicked;

    public event EventHandler? ViewChanged;

    public TgaImage? Image => _image;

    public double Zoom => _view.Zoom;

    public bool IsFitMode => _fitMode;

    public string Placeholder => "Open a TGA image with Ctrl+O, or drop one here.";

    /// <summary>
    /// Shows a new image, rendering it before anything else changes. If rendering fails, the
    /// previous image stays and the exception is passed on.
    /// </summary>
    public void SetImage(TgaImage? image, AlphaInterpretation alpha)
    {
        var previous = (_image, _alpha, _view, _fitMode);
        _image = image;
        _alpha = alpha;
        _fitMode = true;
        try
        {
            if (image is not null)
            {
                _view = ViewMath.Fit(image.Width, image.Height, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
                RenderNow();
            }
        }
        catch
        {
            (_image, _alpha, _view, _fitMode) = previous;
            _dirty = true;
            Invalidate();
            throw;
        }

        AccessibleDescription = image is null ? null : $"{image.Width} by {image.Height} pixels, {image.FormatDescription}";
        Changed();
    }

    public void SetAlpha(AlphaInterpretation alpha)
    {
        _alpha = alpha;
        Changed();
    }

    public void SetOptions(DisplayOptions options)
    {
        _options = options;
        Changed();
    }

    public void SetMessages(string? banner, string? overlay)
    {
        _banner = banner;
        _overlay = overlay;
        Invalidate();
    }

    public void FitToWindow()
    {
        _fitMode = true;
        if (_image is not null)
        {
            _view = ViewMath.Fit(_image.Width, _image.Height, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
        }

        Changed();
    }

    public void ActualPixels(Point? anchor = null) => ZoomTo(1, anchor);

    public void ZoomIn(Point? anchor = null) => ZoomTo(ViewMath.Step(_view.Zoom, ZoomStep), anchor);

    public void ZoomOut(Point? anchor = null) => ZoomTo(ViewMath.Step(_view.Zoom, 1 / ZoomStep), anchor);

    public void PanBy(int dx, int dy)
    {
        if (_image is null)
        {
            return;
        }

        _fitMode = false;
        _view = Clamp(_view with { OriginX = _view.OriginX + dx, OriginY = _view.OriginY + dy });
        Changed(interactive: true);
    }

    public Point? PixelAt(Point client) =>
        _image is not null && ViewMath.ImagePixelAt(_view, client.X, client.Y, _image.Width, _image.Height) is { } p
            ? new Point(p.X, p.Y)
            : null;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int width = ClientSize.Width;
        int height = ClientSize.Height;
        if (_image is null || width <= 0 || height <= 0)
        {
            // Empty state: a large faint picture icon above the hint.
            g.Clear(BackColor);
            int icon = LogicalToDeviceUnits(64);
            int gap = LogicalToDeviceUnits(12);
            Theme.DrawGlyph(g, Theme.GlyphImage, new Rectangle((width - icon) / 2, height / 2 - icon - gap / 2, icon, icon), icon * 0.85F, Theme.TextFaint);
            TextRenderer.DrawText(g, Placeholder, Theme.PlaceholderFont, new Rectangle(0, height / 2 + gap / 2, width, height / 2 - gap), Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
            return;
        }

        if (_dirty || _bufferWidth != width || _bufferHeight != height)
        {
            RenderNow();
        }

        // The buffer is already exactly the size of the control: copy it unchanged.
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_bitmap!, new Rectangle(0, 0, width, height), 0, 0, width, height, GraphicsUnit.Pixel);
        g.CompositingMode = CompositingMode.SourceOver;
        DrawMessages(g);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left && _image is not null)
        {
            _dragging = true;
            _dragMoved = false;
            _dragStart = e.Location;
            _dragView = _view;
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            int dx = e.X - _dragStart.X;
            int dy = e.Y - _dragStart.Y;
            if (!_dragMoved && Math.Abs(dx) + Math.Abs(dy) >= SystemInformation.DragSize.Width)
            {
                _dragMoved = true;
                Cursor = Cursors.SizeAll;
            }

            if (_dragMoved)
            {
                _fitMode = false;
                _view = Clamp(_dragView with { OriginX = _dragView.OriginX + dx, OriginY = _dragView.OriginY + dy });
                Changed(interactive: true);
            }
        }

        PixelHovered?.Invoke(this, new PixelEventArgs(PixelAt(e.Location)));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        Capture = false;
        Cursor = Cursors.Default;
        if (!_dragMoved && e.Button == MouseButtons.Left)
        {
            PixelClicked?.Invoke(this, new PixelEventArgs(PixelAt(e.Location)));
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_image is not null && e.Delta != 0)
        {
            ZoomTo(ViewMath.Step(_view.Zoom, Math.Pow(ZoomStep, e.Delta / 120.0)), e.Location);
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (_image is null || e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_fitMode)
        {
            ZoomTo(1, e.Location);
        }
        else
        {
            FitToWindow();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        PixelHovered?.Invoke(this, new PixelEventArgs(null));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_image is null)
        {
            return;
        }

        _view = _fitMode
            ? ViewMath.Fit(_image.Width, _image.Height, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height))
            : Clamp(_view);
        Changed();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        _dirty = true;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _settleTimer.Dispose();
            FreeBuffer();
        }

        base.Dispose(disposing);
    }

    private void ZoomTo(double zoom, Point? anchor)
    {
        if (_image is null)
        {
            return;
        }

        double fit = ViewMath.FitZoom(_image.Width, _image.Height, ClientSize.Width, ClientSize.Height);
        zoom = Math.Clamp(zoom, Math.Min(0.01, fit), Math.Max(256, fit));
        var point = anchor ?? new Point(ClientSize.Width / 2, ClientSize.Height / 2);
        _fitMode = false;
        _view = Clamp(ViewMath.ZoomAt(_view, zoom, point.X, point.Y));
        Changed(interactive: true);
    }

    private ViewTransform Clamp(ViewTransform view) =>
        _image is null ? view : ViewMath.Clamp(view, _image.Width, _image.Height, ClientSize.Width, ClientSize.Height);

    private void Changed(bool interactive = false)
    {
        _dirty = true;
        if (interactive && _options.Scaling == ScalingMode.Smooth)
        {
            _interacting = true;
            _settleTimer.Stop();
            _settleTimer.Start();
        }

        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RenderNow()
    {
        int width = Math.Max(1, ClientSize.Width);
        int height = Math.Max(1, ClientSize.Height);
        EnsureBuffer(width, height);
        var options = _options with
        {
            CheckerSize = Math.Max(1, (int)Math.Round(8 * DeviceDpi / 96.0)),
            Scaling = _interacting ? ScalingMode.Nearest : _options.Scaling,
            CanvasColor = 0xFF000000 | (uint)(BackColor.ToArgb() & 0xFFFFFF),
        };
        DisplayRenderer.Render(_image!, _alpha, options, _view, _buffer!, width, height, width);
        _dirty = false;
    }

    private void EnsureBuffer(int width, int height)
    {
        if (_bitmap is not null && _bufferWidth == width && _bufferHeight == height)
        {
            return;
        }

        FreeBuffer();

        // An array on the pinned object heap lets the bitmap use the rendered pixels directly, with no copy.
        _buffer = GC.AllocateUninitializedArray<uint>(width * height, pinned: true);
        _bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppRgb, Marshal.UnsafeAddrOfPinnedArrayElement(_buffer, 0));
        _bufferWidth = width;
        _bufferHeight = height;
    }

    private void FreeBuffer()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _buffer = null;
        _bufferWidth = 0;
        _bufferHeight = 0;
    }

    private void DrawMessages(Graphics g)
    {
        if (!string.IsNullOrEmpty(_banner))
        {
            // A warning strip across the top: amber edge and icon, then the message.
            int pad = LogicalToDeviceUnits(10);
            int icon = LogicalToDeviceUnits(16);
            int textLeft = pad * 2 + icon;
            var size = TextRenderer.MeasureText(g, _banner, Font, new Size(Math.Max(1, ClientSize.Width - textLeft - pad), int.MaxValue), TextFormatFlags.WordBreak);
            var area = new Rectangle(0, 0, ClientSize.Width, size.Height + pad * 2);
            using (var brush = new SolidBrush(Color.FromArgb(238, Theme.Chrome)))
            {
                g.FillRectangle(brush, area);
            }

            using (var edge = new SolidBrush(Theme.Warning))
            {
                g.FillRectangle(edge, 0, 0, LogicalToDeviceUnits(3), area.Height);
            }

            using (var line = new Pen(Theme.Border))
            {
                g.DrawLine(line, 0, area.Bottom - 1, area.Right, area.Bottom - 1);
            }

            Theme.DrawGlyph(g, Theme.GlyphWarning, new Rectangle(pad + LogicalToDeviceUnits(2), pad, icon, Math.Min(icon + 2, area.Height - pad)), icon * 0.9F, Theme.Warning);
            TextRenderer.DrawText(g, _banner, Font, Rectangle.FromLTRB(textLeft, pad, area.Right - pad, area.Bottom - pad), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        }

        if (!string.IsNullOrEmpty(_overlay))
        {
            // A message card in the middle of the view.
            var size = TextRenderer.MeasureText(g, _overlay, Font);
            int padX = LogicalToDeviceUnits(20);
            int padY = LogicalToDeviceUnits(14);
            var area = new Rectangle((ClientSize.Width - size.Width) / 2 - padX, (ClientSize.Height - size.Height) / 2 - padY, size.Width + padX * 2, size.Height + padY * 2);
            Theme.FillRounded(g, area, LogicalToDeviceUnits(6), Color.FromArgb(242, Theme.Control));
            using (var path = Theme.Rounded(area, LogicalToDeviceUnits(6)))
            using (var pen = new Pen(Theme.Border))
            {
                var smoothing = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawPath(pen, path);
                g.SmoothingMode = smoothing;
            }

            TextRenderer.DrawText(g, _overlay, Font, area, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
