namespace TGAPeek.Core.Decoding;

internal static class RowOrder
{
    /// <summary>
    /// For each stored row k (file order), the image row it belongs to (row 0 is the top).
    /// Interleaving follows TGA 1.0 as implemented by netpbm's tgatoppm: stored rows fill logical
    /// rows 0, s, 2s, … then 1, 1 + s, … (s = 2 for two-way, 4 for four-way). The bottom-origin
    /// flip (descriptor bit 5 clear) is applied afterwards.
    /// </summary>
    public static int[] Build(int height, TgaInterleave interleave, bool topToBottom)
    {
        int step = interleave switch
        {
            TgaInterleave.TwoWay => 2,
            TgaInterleave.FourWay => 4,
            _ => 1,
        };

        var map = new int[height];
        int logicalRow = 0;
        int pass = 0;
        for (int k = 0; k < height; k++)
        {
            map[k] = topToBottom ? logicalRow : height - 1 - logicalRow;
            logicalRow += step;
            if (logicalRow >= height)
            {
                logicalRow = ++pass;
            }
        }

        return map;
    }

    public static int[] Invert(int[] map)
    {
        var inverse = new int[map.Length];
        for (int k = 0; k < map.Length; k++)
        {
            inverse[map[k]] = k;
        }

        return inverse;
    }
}
