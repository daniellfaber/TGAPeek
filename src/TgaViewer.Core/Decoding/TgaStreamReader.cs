namespace TgaViewer.Core.Decoding;

/// <summary>
/// Sequential, buffered reads from a stream that never go past <c>limit</c> bytes from where it
/// started. Short reads at the end are reported through return values, never by reading further.
/// </summary>
internal sealed class TgaStreamReader
{
    private readonly Stream _stream;
    private readonly byte[] _buffer;
    private readonly long _limit;
    private long _bufferStart;
    private int _position;
    private int _length;
    private bool _ended;

    public TgaStreamReader(Stream stream, long limit, int bufferSize = TgaMemoryEstimator.ReadBufferSize)
    {
        _stream = stream;
        _limit = limit;
        _buffer = new byte[bufferSize];
    }

    /// <summary>Bytes consumed so far, relative to where reading started.</summary>
    public long Position => _bufferStart + _position;

    public int ReadByte()
    {
        if (_position >= _length && !Fill())
        {
            return -1;
        }

        return _buffer[_position++];
    }

    /// <summary>Reads up to <paramref name="destination"/>.Length bytes; fewer only at the end of the data.</summary>
    public int Read(Span<byte> destination)
    {
        int total = 0;
        while (total < destination.Length)
        {
            if (_position >= _length && !Fill())
            {
                break;
            }

            int count = Math.Min(destination.Length - total, _length - _position);
            _buffer.AsSpan(_position, count).CopyTo(destination[total..]);
            _position += count;
            total += count;
        }

        return total;
    }

    public long Skip(long count)
    {
        long skipped = 0;
        while (skipped < count)
        {
            if (_position >= _length && !Fill())
            {
                break;
            }

            int step = (int)Math.Min(count - skipped, _length - _position);
            _position += step;
            skipped += step;
        }

        return skipped;
    }

    private bool Fill()
    {
        if (_ended)
        {
            return false;
        }

        _bufferStart += _length;
        _position = 0;
        _length = 0;
        long remaining = _limit - _bufferStart;
        if (remaining <= 0)
        {
            _ended = true;
            return false;
        }

        int read = _stream.Read(_buffer, 0, (int)Math.Min(_buffer.Length, remaining));
        if (read <= 0)
        {
            _ended = true;
            return false;
        }

        _length = read;
        return true;
    }
}
