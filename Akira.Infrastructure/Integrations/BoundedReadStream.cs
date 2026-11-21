namespace Akira.Infrastructure.Integrations;

/// <summary>
/// Reads another stream up to a number of bytes and fails past it, so a body is never larger in memory than was allowed for.
/// </summary>
internal sealed class BoundedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private long _read;

    public BoundedReadStream(Stream inner, long maxBytes)
    {
        _inner = inner;
        _maxBytes = maxBytes;
    }

    public bool Exceeded { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _read;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Count(_inner.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await _inner.ReadAsync(buffer, cancellationToken));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private int Count(int read)
    {
        _read += read;
        if (_read > _maxBytes)
        {
            Exceeded = true;
            throw new IOException($"The body is larger than {_maxBytes} bytes.");
        }

        return read;
    }
}
