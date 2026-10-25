namespace TgaViewer.Core;

/// <summary>Zoom and pan arithmetic for the viewer, in device pixels.</summary>
public static class ViewMath
{
    public static double FitZoom(int imageWidth, int imageHeight, int viewWidth, int viewHeight) =>
        imageWidth <= 0 || imageHeight <= 0 || viewWidth <= 0 || viewHeight <= 0
            ? 1
            : Math.Min((double)viewWidth / imageWidth, (double)viewHeight / imageHeight);

    /// <summary>The whole image fitted to the view, keeping its aspect ratio, centred.</summary>
    public static ViewTransform Fit(int imageWidth, int imageHeight, int viewWidth, int viewHeight) =>
        Centre(FitZoom(imageWidth, imageHeight, viewWidth, viewHeight), imageWidth, imageHeight, viewWidth, viewHeight);

    public static ViewTransform Centre(double zoom, int imageWidth, int imageHeight, int viewWidth, int viewHeight) =>
        new(zoom, Math.Round((viewWidth - imageWidth * zoom) / 2), Math.Round((viewHeight - imageHeight * zoom) / 2));

    /// <summary>Changes the zoom while keeping the image point under (screenX, screenY) in place.</summary>
    public static ViewTransform ZoomAt(ViewTransform view, double zoom, double screenX, double screenY)
    {
        double imageX = (screenX - view.OriginX) / view.Zoom;
        double imageY = (screenY - view.OriginY) / view.Zoom;
        return new ViewTransform(zoom, screenX - imageX * zoom, screenY - imageY * zoom);
    }

    /// <summary>Centres an axis that fits in the view; otherwise keeps the view covered by the image.</summary>
    public static ViewTransform Clamp(ViewTransform view, int imageWidth, int imageHeight, int viewWidth, int viewHeight) =>
        view with
        {
            OriginX = ClampAxis(view.OriginX, imageWidth * view.Zoom, viewWidth),
            OriginY = ClampAxis(view.OriginY, imageHeight * view.Zoom, viewHeight),
        };

    /// <summary>The image pixel shown at device pixel (screenX, screenY), or null outside the image (same rule as the renderer).</summary>
    public static (int X, int Y)? ImagePixelAt(ViewTransform view, int screenX, int screenY, int imageWidth, int imageHeight)
    {
        double x = Math.Floor((screenX + 0.5 - view.OriginX) / view.Zoom);
        double y = Math.Floor((screenY + 0.5 - view.OriginY) / view.Zoom);
        return x >= 0 && x < imageWidth && y >= 0 && y < imageHeight ? ((int)x, (int)y) : null;
    }

    /// <summary>The next zoom level when zooming by <paramref name="factor"/>, stopping at 100% when it is crossed.</summary>
    public static double Step(double zoom, double factor)
    {
        double next = zoom * factor;
        return (zoom < 1 && next > 1) || (zoom > 1 && next < 1) ? 1 : next;
    }

    private static double ClampAxis(double origin, double size, int view) =>
        size <= view ? Math.Round((view - size) / 2) : Math.Clamp(origin, view - size, 0);
}
