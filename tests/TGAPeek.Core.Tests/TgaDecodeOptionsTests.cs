using TGAPeek.Core;

namespace TGAPeek.Core.Tests;

public class TgaDecodeOptionsTests
{
    [Fact]
    public void Default_AcceptsEveryValidSize_AndUsesTheMemoryBudget()
    {
        var options = TgaDecodeOptions.Default;

        Assert.Equal(65_535, options.MaxWidth);
        Assert.Equal(65_535, options.MaxHeight);
        Assert.Equal(65_535L * 65_535L, options.MaxPixelCount);
        Assert.Equal(4L << 30, options.MaxDecodeMemoryBytes);
        Assert.False(options.AllowPartial);
    }

    [Fact]
    public void Limits_AreConfigurable()
    {
        var options = TgaDecodeOptions.Default with { MaxWidth = 64, AllowPartial = true };

        Assert.Equal(64, options.MaxWidth);
        Assert.True(options.AllowPartial);
        Assert.Equal(65_535, TgaDecodeOptions.Default.MaxWidth);
    }
}
