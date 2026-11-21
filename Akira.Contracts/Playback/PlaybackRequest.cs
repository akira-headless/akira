namespace Akira.Contracts.Playback;

/// <summary>
/// Represents a request to play an item. FileId picks a version, otherwise the server picks the one that suits the
/// client best.
/// </summary>
public sealed record PlaybackRequest(Guid ItemId, ClientCapabilities Capabilities)
{
    public Guid? FileId { get; init; }
}
