using System.Threading.Channels;

namespace Akira.Server.Webhooks;

/// <summary>
/// Holds the movies and series webhooks asked to refresh until the refresh worker takes them.
/// </summary>
public sealed class LibraryRefreshQueue
{
    private int _urgent;

    // A full queue drops the refresh. The next scheduled full sync picks the change up anyway.
    private readonly Channel<LibraryRefresh> _channel = Channel.CreateBounded<LibraryRefresh>(
        new BoundedChannelOptions(10_000) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });

    public ChannelReader<LibraryRefresh> Reader => _channel.Reader;

    public bool Enqueue(LibraryRefresh refresh) => _channel.Writer.TryWrite(refresh);

    /// <summary>
    /// Enqueues a refresh the worker runs without its wait, for a change a user is looking at.
    /// </summary>
    public bool EnqueueNow(LibraryRefresh refresh)
    {
        Interlocked.Exchange(ref _urgent, 1);
        return _channel.Writer.TryWrite(refresh);
    }

    public bool TakeUrgent() => Interlocked.Exchange(ref _urgent, 0) == 1;
}
