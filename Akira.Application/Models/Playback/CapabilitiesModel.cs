namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents what a client says it can play. A list left out means the client did not say, so anything goes.
/// AccessModes names filePath, http or hls.
/// </summary>
public sealed record CapabilitiesModel(
    IReadOnlyList<string>? VideoCodecs,
    IReadOnlyList<string>? Hdr,
    IReadOnlyList<string>? AudioPassthrough,
    IReadOnlyList<string>? AudioDecode,
    IReadOnlyList<string>? SubtitleFormats,
    IReadOnlyList<string>? AccessModes,
    int? MaxHeight,
    IReadOnlyList<string>? Containers = null,
    int? MaxBitrateKbps = null);
