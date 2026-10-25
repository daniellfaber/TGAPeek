namespace TGAPeek.Core.Tests;

public class ViewMathTests
{
    [Fact]
    public void Fit_KeepsAspectRatio_AndCentres()
    {
        var view = ViewMath.Fit(200, 100, 400, 400);
        Assert.Equal(new ViewTransform(2, 0, 100), view);

        var small = ViewMath.Fit(1000, 500, 100, 100);
        Assert.Equal(0.1, small.Zoom, 9);
        Assert.Equal(25, small.OriginY);
    }

    [Fact]
    public void ZoomAt_KeepsThePointUnderTheCursorStill()
    {
        var before = new ViewTransform(1.5, 10, -20);
        var after = ViewMath.ZoomAt(before, 4, 300, 200);
        double imageXBefore = (300 - before.OriginX) / before.Zoom;
        double imageXAfter = (300 - after.OriginX) / after.Zoom;
        Assert.Equal(imageXBefore, imageXAfter, 9);
        Assert.Equal((200 - before.OriginY) / before.Zoom, (200 - after.OriginY) / after.Zoom, 9);
    }

    [Fact]
    public void Clamp_CentresSmallImages_AndKeepsLargeImagesCoveringTheView()
    {
        Assert.Equal(new ViewTransform(1, 50, 25), ViewMath.Clamp(new ViewTransform(1, -300, 900), 100, 50, 200, 100));
        var large = ViewMath.Clamp(new ViewTransform(2, 100, -5000), 1000, 1000, 400, 300);
        Assert.Equal(0, large.OriginX);
        Assert.Equal(300 - 2000, large.OriginY);
    }

    [Theory]
    [InlineData(1.0, 0.0, 0.0)]
    [InlineData(3.7, -12.3, 4.6)]
    [InlineData(0.25, 3.0, 1.0)]
    public void ImagePixelAt_MatchesTheRenderer(double zoom, double originX, double originY)
    {
        var image = TgaDecoder.Decode(TGAPeek.TestSupport.LargeTga.Uncompressed32(16, 16));
        var view = new ViewTransform(zoom, originX, originY);
        var buffer = new uint[40 * 40];
        DisplayRenderer.Render(image, AlphaInterpretation.Opaque, new DisplayOptions { CanvasColor = 0xFF000000 }, view, buffer, 40, 40, 40);
        for (int sy = 0; sy < 40; sy++)
        {
            for (int sx = 0; sx < 40; sx++)
            {
                var pixel = ViewMath.ImagePixelAt(view, sx, sy, 16, 16);
                uint expected = pixel is { } p ? image.GetPixel(p.X, p.Y) | 0xFF000000 : 0xFF000000;
                Assert.Equal(expected, buffer[sy * 40 + sx]);
            }
        }
    }

    [Fact]
    public void Step_StopsAtOneHundredPercent()
    {
        Assert.Equal(1, ViewMath.Step(0.9, 1.25));
        Assert.Equal(1, ViewMath.Step(1.1, 0.8));
        Assert.Equal(1.25, ViewMath.Step(1, 1.25));
        Assert.Equal(0.5, ViewMath.Step(0.4, 1.25), 9);
    }
}
