namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents a way a client can open the version. Mode is filePath with a Path, or http or hls with a Url.
/// </summary>
public sealed record PlanSource(SourceMode Mode, string? Path, string? Url, bool SupportsRange);
