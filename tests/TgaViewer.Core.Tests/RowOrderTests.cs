using TgaViewer.Core.Decoding;
using TgaViewer.TestSupport;

namespace TgaViewer.Core.Tests;

public class RowOrderTests
{
    [Theory]
    [InlineData(TgaInterleave.None)]
    [InlineData(TgaInterleave.TwoWay)]
    [InlineData(TgaInterleave.FourWay)]
    public void EveryImageRowIsFilledExactlyOnce(TgaInterleave interleave)
    {
        for (int height = 1; height <= 300; height++)
        {
            foreach (bool topToBottom in new[] { false, true })
            {
                int[] map = RowOrder.Build(height, interleave, topToBottom);
                Assert.Equal(Enumerable.Range(0, height), map.Order());
            }
        }
    }

    [Fact]
    public void HandWorkedExamples()
    {
        Assert.Equal(new[] { 0, 2, 4, 1, 3 }, RowOrder.Build(5, TgaInterleave.TwoWay, topToBottom: true));
        Assert.Equal(new[] { 4, 2, 0, 3, 1 }, RowOrder.Build(5, TgaInterleave.TwoWay, topToBottom: false));
        Assert.Equal(new[] { 0, 4, 1, 5, 2, 6, 3 }, RowOrder.Build(7, TgaInterleave.FourWay, topToBottom: true));
        Assert.Equal(new[] { 6, 2, 5, 1, 4, 0, 3 }, RowOrder.Build(7, TgaInterleave.FourWay, topToBottom: false));
        Assert.Equal(new[] { 2, 1, 0 }, RowOrder.Build(3, TgaInterleave.None, topToBottom: false));
    }

    [Fact]
    public void MatchesTheSeparateNetpbmPort()
    {
        for (int height = 1; height <= 200; height++)
        {
            for (int interleave = 0; interleave <= 2; interleave++)
            {
                foreach (bool topToBottom in new[] { false, true })
                {
                    Assert.Equal(
                        TgaBuilder.StoredRowOrder(height, interleave, topToBottom),
                        RowOrder.Build(height, (TgaInterleave)interleave, topToBottom));
                }
            }
        }
    }
}
