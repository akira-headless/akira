namespace Akira.Infrastructure.Interfaces.Probing;

/// <summary>
/// Represents a subtitle track of a version that is a file of its own, by its stream id and path.
/// </summary>
public sealed record ExternalTrack(string StreamId, string Path);
