namespace TgaViewer.Core.Decoding;

/// <summary>
/// Receives decoded pixels in file order, one stored row at a time, and places each completed row
/// at its image position (handling origin and interleaving). Every write is bounded by the image
/// size, so malformed data can never write outside the buffers.
/// </summary>
internal sealed class PixelSink
{
    private readonly int _width;
    private readonly int _height;
    private readonly bool _rightToLeft;
    private readonly PixelLayout _layout;
    private readonly TgaPalette? _palette;
    private readonly int[] _rowMap;
    private readonly uint[] _rowPixels;
    private readonly ushort[]? _rowIndices;
    private readonly bool[]? _rowInvalid;
    private bool _rowHasInvalid;
    private int _fill;
    private int _storedRow;
    private uint _andAll = uint.MaxValue;
    private uint _orAll;
    private long _validPixels;

    public PixelSink(TgaHeader header, PixelLayout layout, TgaPalette? palette)
    {
        _width = header.Width;
        _height = header.Height;
        _rightToLeft = header.RightToLeft;
        _layout = layout;
        _palette = palette;
        _rowMap = RowOrder.Build(_height, header.Interleave, header.TopToBottom);
        StoredRowOfImageRow = RowOrder.Invert(_rowMap);

        Rows = new uint[_height][];
        for (int y = 0; y < _height; y++)
        {
            Rows[y] = new uint[_width];
        }

        _rowPixels = new uint[_width];
        if (palette is not null)
        {
            _rowIndices = new ushort[_width];
            _rowInvalid = new bool[_width];
            if (layout == PixelLayout.Index8)
            {
                Indices8 = new byte[_height][];
                for (int y = 0; y < _height; y++)
                {
                    Indices8[y] = new byte[_width];
                }
            }
            else
            {
                Indices16 = new ushort[_height][];
                for (int y = 0; y < _height; y++)
                {
                    Indices16[y] = new ushort[_width];
                }
            }
        }
    }

    public uint[][] Rows { get; }

    public byte[][]? Indices8 { get; }

    public ushort[][]? Indices16 { get; }

    public int[] StoredRowOfImageRow { get; }

    public TgaBitMask? MissingMask { get; private set; }

    public TgaBitMask? InvalidMask { get; private set; }

    public long DecodedPixels { get; private set; }

    public long InvalidPixels { get; private set; }

    public int RowRemaining => _width - _fill;

    public bool IsComplete => _storedRow >= _height;

    public bool AllAlphaZero => _validPixels > 0 && _orAll >> 24 == 0;

    public bool AllAlphaOpaque => _validPixels > 0 && _andAll >> 24 == 0xFF;

    /// <summary>Appends <paramref name="count"/> stored pixels (count ≤ <see cref="RowRemaining"/>).</summary>
    public void Append(ReadOnlySpan<byte> stored, int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (_palette is null)
        {
            PixelConversion.ConvertDirect(_layout, stored, _rowPixels.AsSpan(_fill, count));
        }
        else
        {
            PixelConversion.ReadIndices(_layout, stored, _rowIndices.AsSpan(_fill, count));
            for (int i = _fill; i < _fill + count; i++)
            {
                LookUp(i, _rowIndices![i]);
            }
        }

        DecodedPixels += count;
        Advance(count);
    }

    /// <summary>Appends one stored pixel <paramref name="count"/> times (an RLE run packet), continuing into following rows.</summary>
    public void AppendRun(ReadOnlySpan<byte> stored, int count)
    {
        uint value;
        ushort index = 0;
        bool invalid = false;
        if (_palette is null)
        {
            Span<uint> one = stackalloc uint[1];
            PixelConversion.ConvertDirect(_layout, stored, one);
            value = one[0];
        }
        else
        {
            Span<ushort> one = stackalloc ushort[1];
            PixelConversion.ReadIndices(_layout, stored, one);
            index = one[0];
            invalid = !_palette.Contains(index);
            value = invalid ? 0 : _palette[index];
        }

        while (count > 0 && !IsComplete)
        {
            int n = Math.Min(count, RowRemaining);
            _rowPixels.AsSpan(_fill, n).Fill(value);
            if (_rowIndices is not null)
            {
                _rowIndices.AsSpan(_fill, n).Fill(index);
                if (invalid)
                {
                    _rowInvalid.AsSpan(_fill, n).Fill(true);
                    _rowHasInvalid = true;
                }
            }

            DecodedPixels += n;
            count -= n;
            Advance(n);
        }
    }

    /// <summary>Called when the data ends early: keeps what was decoded and marks every remaining pixel as missing.</summary>
    public void Finish()
    {
        if (IsComplete)
        {
            return;
        }

        int filled = _fill;
        int partialRow = _storedRow;
        CommitRow(filled);
        MissingMask ??= new TgaBitMask((long)_width * _height);
        long rowStart = (long)_rowMap[partialRow] * _width;
        for (int c = filled; c < _width; c++)
        {
            MissingMask.Set(rowStart + X(c));
        }

        for (int k = _storedRow; k < _height; k++)
        {
            rowStart = (long)_rowMap[k] * _width;
            for (int x = 0; x < _width; x++)
            {
                MissingMask.Set(rowStart + x);
            }
        }

        _storedRow = _height;
    }

    private void LookUp(int column, int index)
    {
        if (_palette!.Contains(index))
        {
            _rowPixels[column] = _palette[index];
        }
        else
        {
            _rowPixels[column] = 0;
            _rowInvalid![column] = true;
            _rowHasInvalid = true;
        }
    }

    private void Advance(int count)
    {
        _fill += count;
        if (_fill == _width)
        {
            CommitRow(_width);
        }
    }

    private int X(int column) => _rightToLeft ? _width - 1 - column : column;

    private void CommitRow(int filled)
    {
        int y = _rowMap[_storedRow];
        uint[] destination = Rows[y];
        if (_rightToLeft)
        {
            for (int c = 0; c < filled; c++)
            {
                destination[_width - 1 - c] = _rowPixels[c];
            }
        }
        else
        {
            _rowPixels.AsSpan(0, filled).CopyTo(destination);
        }

        if (Indices8 is not null)
        {
            byte[] row = Indices8[y];
            for (int c = 0; c < filled; c++)
            {
                row[X(c)] = (byte)_rowIndices![c];
            }
        }
        else if (Indices16 is not null)
        {
            ushort[] row = Indices16[y];
            for (int c = 0; c < filled; c++)
            {
                row[X(c)] = _rowIndices![c];
            }
        }

        for (int c = 0; c < filled; c++)
        {
            if (_rowHasInvalid && _rowInvalid![c])
            {
                _rowInvalid[c] = false;
                InvalidMask ??= new TgaBitMask((long)_width * _height);
                InvalidMask.Set((long)y * _width + X(c));
                InvalidPixels++;
                continue;
            }

            uint pixel = _rowPixels[c];
            _andAll &= pixel;
            _orAll |= pixel;
            _validPixels++;
        }

        _rowHasInvalid = false;
        _storedRow++;
        _fill = 0;
    }
}
