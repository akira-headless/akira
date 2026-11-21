namespace Akira.Server.Bridges;

/// <summary>
/// Passes what is written on and counts the bytes, so the bytes a renderer took of a file say how far into it it got.
/// </summary>
public sealed class CountingStream : Stream
{
    private readonly Stream _inner;
    private long _written;

    public CountingStream(Stream inner)
    {
        _inner = inner;
    }

    public long Written => Interlocked.Read(ref _written);

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
        _inner.Write(buffer, offset, count);
        Interlocked.Add(ref _written, count);
    }

    /// <inheritdoc />
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        Interlocked.Add(ref _written, count);
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _inner.WriteAsync(buffer, cancellationToken);
        Interlocked.Add(ref _written, buffer.Length);
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
}
