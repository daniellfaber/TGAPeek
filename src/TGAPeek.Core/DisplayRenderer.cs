namespace TGAPeek.Core;

public enum ChannelView
{
    /// <summary>Colour composited with alpha over the background.</summary>
    Rgba,

    /// <summary>Colour channels only, alpha not applied.</summary>
    Rgb,
    Red,
    Green,
    Blue,
    Alpha,
}

public enum ScalingMode
{
    Nearest,
    Smooth,
}

public sealed record DisplayOptions
{
    public ChannelView Channel { get; init; } = ChannelView.Rgba;
    public ScalingMode Scaling { get; init; } = ScalingMode.Nearest;
    public bool Checkerboard { get; init; } = true;
    public uint SolidBackground { get; init; } = 0xFF808080;
    public uint CheckerLight { get; init; } = 0xFFFFFFFF;
    public uint CheckerDark { get; init; } = 0xFFCCCCCC;

    /// <summary>Checkerboard cell size in device pixels.</summary>
    public int CheckerSize { get; init; } = 8;

    /// <summary>Colour outside the image.</summary>
    public uint CanvasColor { get; init; } = 0xFF2B2B2B;
}

/// <summary>Maps image pixels to device pixels: screen = Origin + image × Zoom.</summary>
public readonly record struct ViewTransform(double Zoom, double OriginX, double OriginY);

/// <summary>
/// Renders the visible part of an image into an opaque 0xFFRRGGBB buffer. All compositing happens
/// here, so the buffer handed to Windows never carries alpha and is drawn without scaling. Stored
/// pixels are only read.
/// </summary>
public static class DisplayRenderer
{
    public const uint MissingStripe1 = 0xFFFF00FF;
    public const uint MissingStripe2 = 0xFF400040;
    public const uint InvalidStripe1 = 0xFFFF8C00;
    public const uint InvalidStripe2 = 0xFF402300;
    public const uint NoAlphaStripe1 = 0xFF3A3A3A;
    public const uint NoAlphaStripe2 = 0xFF4E4E4E;

    private const int BandHeight = 32;

    public static void Render(
        TgaImage image,
        AlphaInterpretation alpha,
        DisplayOptions options,
        ViewTransform view,
        uint[] destination,
        int width,
        int height,
        int stride,
        bool parallel = true)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(destination);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (stride < width || (long)stride * (height - 1) + width > destination.Length)
        {
            throw new ArgumentException("The destination buffer is too small for the requested size.", nameof(destination));
        }

        if (!(view.Zoom > 0) || double.IsInfinity(view.Zoom) || !double.IsFinite(view.OriginX) || !double.IsFinite(view.OriginY))
        {
            throw new ArgumentOutOfRangeException(nameof(view), "The zoom and origin must be finite, and the zoom positive.");
        }

        var job = new RenderJob(image, alpha, options, view, destination, width, height, stride);
        int bands = (height + BandHeight - 1) / BandHeight;
        if (parallel && bands > 1)
        {
            Parallel.For(0, bands, job.RenderBand);
        }
        else
        {
            for (int band = 0; band < bands; band++)
            {
                job.RenderBand(band);
            }
        }
    }

    /// <summary>x / 255, rounded to nearest.</summary>
    public static uint Div255(uint x) => (x + 127) / 255;

    /// <summary>Straight alpha: out = (c·a + bg·(255 − a)) / 255, rounded.</summary>
    public static uint CompositeStraight(uint pixel, uint background)
    {
        uint a = pixel >> 24;
        if (a == 255)
        {
            return pixel;
        }

        if (a == 0)
        {
            return background | 0xFF000000;
        }

        uint ia = 255 - a;
        uint r = Div255(((pixel >> 16) & 0xFF) * a + ((background >> 16) & 0xFF) * ia);
        uint g = Div255(((pixel >> 8) & 0xFF) * a + ((background >> 8) & 0xFF) * ia);
        uint b = Div255((pixel & 0xFF) * a + (background & 0xFF) * ia);
        return 0xFF000000 | r << 16 | g << 8 | b;
    }

    /// <summary>
    /// Premultiplied alpha: c' = min(c, a), out = c' + bg·(255 − a) / 255. Clamping colour to alpha
    /// is the display policy for invalid data (any channel above alpha); valid pixels are unaffected,
    /// and because c' ≤ a the result never exceeds 255.
    /// </summary>
    public static uint CompositePremultiplied(uint pixel, uint background)
    {
        uint a = pixel >> 24;
        uint ia = 255 - a;
        uint r = Math.Min((pixel >> 16) & 0xFF, a) + Div255(((background >> 16) & 0xFF) * ia);
        uint g = Math.Min((pixel >> 8) & 0xFF, a) + Div255(((background >> 8) & 0xFF) * ia);
        uint b = Math.Min(pixel & 0xFF, a) + Div255((background & 0xFF) * ia);
        return 0xFF000000 | r << 16 | g << 8 | b;
    }

    public static uint Composite(uint pixel, AlphaInterpretation alpha, uint background) => alpha switch
    {
        AlphaInterpretation.Straight => CompositeStraight(pixel, background),
        AlphaInterpretation.Premultiplied => CompositePremultiplied(pixel, background),
        _ => pixel | 0xFF000000,
    };

    /// <summary>Premultiplied data is valid when no colour channel exceeds alpha.</summary>
    public static bool IsValidPremultiplied(uint pixel)
    {
        uint a = pixel >> 24;
        return ((pixel >> 16) & 0xFF) <= a && ((pixel >> 8) & 0xFF) <= a && (pixel & 0xFF) <= a;
    }

    public static uint Grey(uint value) => 0xFF000000 | value << 16 | value << 8 | value;

    public static uint ChannelValue(uint pixel, ChannelView channel) => channel switch
    {
        ChannelView.Red => (pixel >> 16) & 0xFF,
        ChannelView.Green => (pixel >> 8) & 0xFF,
        ChannelView.Blue => pixel & 0xFF,
        ChannelView.Alpha => pixel >> 24,
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };

    /// <summary>Display colour of one stored pixel (nearest-neighbour path).</summary>
    public static uint Shade(uint pixel, ChannelView channel, AlphaInterpretation alpha, uint background) => channel switch
    {
        ChannelView.Rgba => Composite(pixel, alpha, background),
        ChannelView.Rgb => pixel | 0xFF000000,
        _ => Grey(ChannelValue(pixel, channel)),
    };

    internal static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    private static uint Stripe(int x, int y, uint first, uint second) => (((x + y) >> 2) & 1) == 0 ? first : second;

    private static int ClampIndex(double value, int max) => value <= 0 ? 0 : value >= max ? max : (int)value;

    private struct Accumulator
    {
        public float R;
        public float G;
        public float B;
        public float A;
    }

    private sealed class RenderJob
    {
        private readonly TgaImage _image;
        private readonly AlphaInterpretation _alpha;
        private readonly DisplayOptions _options;
        private readonly ViewTransform _view;
        private readonly uint[] _destination;
        private readonly int _width;
        private readonly int _height;
        private readonly int _stride;
        private readonly int _cell;
        private readonly long _originX;
        private readonly long _originY;
        private readonly int[] _column;
        private readonly byte[] _columnParity;
        private readonly bool _smooth;
        private readonly bool _noAlpha;
        private readonly bool _masks;
        private readonly int _samples;
        private readonly int[]? _left;
        private readonly int[]? _right;
        private readonly float[]? _fraction;
        private readonly int[]? _boxColumns;

        public RenderJob(TgaImage image, AlphaInterpretation alpha, DisplayOptions options, ViewTransform view, uint[] destination, int width, int height, int stride)
        {
            _image = image;
            _alpha = alpha;
            _options = options;
            _view = view;
            _destination = destination;
            _width = width;
            _height = height;
            _stride = stride;
            _cell = Math.Max(1, options.CheckerSize);
            _originX = (long)Math.Floor(view.OriginX);
            _originY = (long)Math.Floor(view.OriginY);
            _noAlpha = options.Channel == ChannelView.Alpha && !image.Alpha.HasStoredAlpha;
            _masks = image.MissingPixels is not null || image.InvalidIndexPixels is not null;
            _smooth = options.Scaling == ScalingMode.Smooth && !_noAlpha;

            int imageWidth = image.Width;
            _column = new int[width];
            _columnParity = new byte[width];
            for (int sx = 0; sx < width; sx++)
            {
                double ix = Math.Floor((sx + 0.5 - view.OriginX) / view.Zoom);
                _column[sx] = ix >= 0 && ix < imageWidth ? (int)ix : -1;
                _columnParity[sx] = (byte)(FloorDiv(sx - _originX, _cell) & 1);
            }

            if (!_smooth)
            {
                return;
            }

            if (view.Zoom >= 1)
            {
                _left = new int[width];
                _right = new int[width];
                _fraction = new float[width];
                for (int sx = 0; sx < width; sx++)
                {
                    double u = (sx + 0.5 - view.OriginX) / view.Zoom - 0.5;
                    double floor = Math.Floor(u);
                    _left[sx] = ClampIndex(floor, imageWidth - 1);
                    _right[sx] = ClampIndex(floor + 1, imageWidth - 1);
                    _fraction[sx] = (float)(u - floor);
                }
            }
            else
            {
                _samples = Math.Clamp((int)Math.Ceiling(1 / view.Zoom), 1, 4);
                _boxColumns = new int[width * _samples];
                for (int sx = 0; sx < width; sx++)
                {
                    for (int i = 0; i < _samples; i++)
                    {
                        double u = (sx + (i + 0.5) / _samples - view.OriginX) / view.Zoom;
                        _boxColumns[sx * _samples + i] = ClampIndex(Math.Floor(u), imageWidth - 1);
                    }
                }
            }
        }

        public void RenderBand(int band)
        {
            int firstRow = band * BandHeight;
            int endRow = Math.Min(_height, firstRow + BandHeight);
            for (int sy = firstRow; sy < endRow; sy++)
            {
                int rowStart = sy * _stride;
                double iy = Math.Floor((sy + 0.5 - _view.OriginY) / _view.Zoom);
                if (iy < 0 || iy >= _image.Height)
                {
                    _destination.AsSpan(rowStart, _width).Fill(_options.CanvasColor);
                    continue;
                }

                int rowParity = (int)(FloorDiv(sy - _originY, _cell) & 1);
                if (_smooth)
                {
                    SmoothRow(sy, (int)iy, rowStart, rowParity);
                }
                else
                {
                    NearestRow(sy, (int)iy, rowStart, rowParity);
                }
            }
        }

        private uint Background(int sx, int rowParity) =>
            !_options.Checkerboard ? _options.SolidBackground
            : (_columnParity[sx] ^ rowParity) == 0 ? _options.CheckerLight : _options.CheckerDark;

        private bool Flagged(long index, int sx, int sy, out uint hatch)
        {
            if (_image.MissingPixels is { } missing && missing[index])
            {
                hatch = Stripe(sx, sy, MissingStripe1, MissingStripe2);
                return true;
            }

            if (_image.InvalidIndexPixels is { } invalid && invalid[index])
            {
                hatch = Stripe(sx, sy, InvalidStripe1, InvalidStripe2);
                return true;
            }

            hatch = 0;
            return false;
        }

        private bool IsFlagged(long index) =>
            (_image.MissingPixels is { } missing && missing[index]) || (_image.InvalidIndexPixels is { } invalid && invalid[index]);

        private void NearestRow(int sy, int iy, int rowStart, int rowParity)
        {
            uint[] row = _image.RowArray(iy);
            long maskRow = (long)iy * _image.Width;
            for (int sx = 0; sx < _width; sx++)
            {
                int ix = _column[sx];
                uint result;
                if (ix < 0)
                {
                    result = _options.CanvasColor;
                }
                else if (_masks && Flagged(maskRow + ix, sx, sy, out uint hatch))
                {
                    result = hatch;
                }
                else if (_noAlpha)
                {
                    result = Stripe(sx, sy, NoAlphaStripe1, NoAlphaStripe2);
                }
                else
                {
                    result = Shade(row[ix], _options.Channel, _alpha, Background(sx, rowParity));
                }

                _destination[rowStart + sx] = result;
            }
        }

        private void SmoothRow(int sy, int iy, int rowStart, int rowParity)
        {
            int imageWidth = _image.Width;
            int lastRow = _image.Height - 1;
            long maskRow = (long)iy * imageWidth;
            if (_view.Zoom >= 1)
            {
                double v = (sy + 0.5 - _view.OriginY) / _view.Zoom - 0.5;
                double floor = Math.Floor(v);
                float fy = (float)(v - floor);
                int top = ClampIndex(floor, lastRow);
                int bottom = ClampIndex(floor + 1, lastRow);
                uint[] topRow = _image.RowArray(top);
                uint[] bottomRow = _image.RowArray(bottom);
                for (int sx = 0; sx < _width; sx++)
                {
                    int ix = _column[sx];
                    if (ix < 0)
                    {
                        _destination[rowStart + sx] = _options.CanvasColor;
                        continue;
                    }

                    if (_masks && Flagged(maskRow + ix, sx, sy, out uint hatch))
                    {
                        _destination[rowStart + sx] = hatch;
                        continue;
                    }

                    int left = _left![sx];
                    int right = _right![sx];
                    float fx = _fraction![sx];
                    var sum = default(Accumulator);
                    Add(ref sum, topRow[left], top, left, (1 - fx) * (1 - fy));
                    Add(ref sum, topRow[right], top, right, fx * (1 - fy));
                    Add(ref sum, bottomRow[left], bottom, left, (1 - fx) * fy);
                    Add(ref sum, bottomRow[right], bottom, right, fx * fy);
                    _destination[rowStart + sx] = Finish(sum, Background(sx, rowParity));
                }
            }
            else
            {
                int samples = _samples;
                Span<int> rows = stackalloc int[samples];
                for (int j = 0; j < samples; j++)
                {
                    rows[j] = ClampIndex(Math.Floor((sy + (j + 0.5) / samples - _view.OriginY) / _view.Zoom), lastRow);
                }

                float weight = 1f / (samples * samples);
                for (int sx = 0; sx < _width; sx++)
                {
                    int ix = _column[sx];
                    if (ix < 0)
                    {
                        _destination[rowStart + sx] = _options.CanvasColor;
                        continue;
                    }

                    if (_masks && Flagged(maskRow + ix, sx, sy, out uint hatch))
                    {
                        _destination[rowStart + sx] = hatch;
                        continue;
                    }

                    var sum = default(Accumulator);
                    for (int j = 0; j < samples; j++)
                    {
                        uint[] row = _image.RowArray(rows[j]);
                        for (int i = 0; i < samples; i++)
                        {
                            int x = _boxColumns![sx * samples + i];
                            Add(ref sum, row[x], rows[j], x, weight);
                        }
                    }

                    _destination[rowStart + sx] = Finish(sum, Background(sx, rowParity));
                }
            }
        }

        /// <summary>Adds one sample as premultiplied display colour; flagged pixels count as transparent black.</summary>
        private void Add(ref Accumulator sum, uint pixel, int y, int x, float weight)
        {
            if (weight <= 0 || (_masks && IsFlagged((long)y * _image.Width + x)))
            {
                return;
            }

            float r = (pixel >> 16) & 0xFF;
            float g = (pixel >> 8) & 0xFF;
            float b = pixel & 0xFF;
            float a = 255;
            switch (_options.Channel)
            {
                case ChannelView.Rgba when _alpha == AlphaInterpretation.Straight:
                    a = pixel >> 24;
                    float scale = a / 255f;
                    r *= scale;
                    g *= scale;
                    b *= scale;
                    break;
                case ChannelView.Rgba when _alpha == AlphaInterpretation.Premultiplied:
                    a = pixel >> 24;
                    r = MathF.Min(r, a);
                    g = MathF.Min(g, a);
                    b = MathF.Min(b, a);
                    break;
                case ChannelView.Rgba:
                case ChannelView.Rgb:
                    break;
                default:
                    r = g = b = ChannelValue(pixel, _options.Channel);
                    break;
            }

            sum.R += r * weight;
            sum.G += g * weight;
            sum.B += b * weight;
            sum.A += a * weight;
        }

        private static uint Finish(in Accumulator sum, uint background)
        {
            float remaining = 1 - sum.A / 255f;
            uint r = ToByte(sum.R + ((background >> 16) & 0xFF) * remaining);
            uint g = ToByte(sum.G + ((background >> 8) & 0xFF) * remaining);
            uint b = ToByte(sum.B + (background & 0xFF) * remaining);
            return 0xFF000000 | r << 16 | g << 8 | b;
        }

        private static uint ToByte(float value) => value <= 0 ? 0 : value >= 255 ? 255 : (uint)(value + 0.5f);
    }
}
