namespace Akira.Server.Bridges;

/// <summary>
/// Passes an MPEG-TS stream on and reads its clock on the way. The stream keeps the timestamps of the file, so the
/// clock says how far into the file the bytes sent so far reach.
/// </summary>
public sealed class MpegTsClockStream : Stream
{
    private const int PacketLength = 188;

    // As much of a packet as holds the clock: the header, the adaptation field's length and flags, and the PCR base
    private const int HeaderLength = 11;
    private const double ClockRate = 90_000;

    private readonly Stream _inner;
    private readonly byte[] _header = new byte[HeaderLength];
    private int _offset;

    public MpegTsClockStream(Stream inner)
    {
        _inner = inner;
    }

    public double? Seconds { get; private set; }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        Track(buffer.AsSpan(offset, count));
        _inner.Write(buffer, offset, count);
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Track(buffer.AsSpan(offset, count));
        return _inner.WriteAsync(buffer, offset, count, cancellationToken);
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Track(buffer.Span);
        return _inner.WriteAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public override void Flush() => _inner.Flush();

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    // A write ends anywhere in a packet, so the start of each packet is collected across writes
    private void Track(ReadOnlySpan<byte> bytes)
    {
        while (bytes.Length > 0)
        {
            if (_offset < HeaderLength)
            {
                var take = Math.Min(HeaderLength - _offset, bytes.Length);
                bytes[..take].CopyTo(_header.AsSpan(_offset));
                _offset += take;
                bytes = bytes[take..];
                if (_offset == HeaderLength)
                {
                    ReadClock();
                }

                continue;
            }

            var skip = Math.Min(PacketLength - _offset, bytes.Length);
            _offset = (_offset + skip) % PacketLength;
            bytes = bytes[skip..];
        }
    }

    private void ReadClock()
    {
        var hasAdaptationField = (_header[3] & 0x20) != 0;
        if (_header[0] != 0x47 || !hasAdaptationField || _header[4] < 7 || (_header[5] & 0x10) == 0)
        {
            return;
        }

        var ticks = ((long)_header[6] << 25) | ((long)_header[7] << 17) | ((long)_header[8] << 9) | ((long)_header[9] << 1) | ((long)_header[10] >> 7);
        Seconds = ticks / ClockRate;
    }
}
