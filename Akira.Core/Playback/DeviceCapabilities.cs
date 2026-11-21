namespace Akira.Core.Playback;

/// <summary>
/// Represents what a client says it can play. A list left out means the client did not say, so anything goes.
/// </summary>
public sealed record DeviceCapabilities(
    IReadOnlyList<string>? VideoCodecs = null,
    IReadOnlyList<string>? Hdr = null,
    IReadOnlyList<string>? AudioPassthrough = null,
    IReadOnlyList<string>? AudioDecode = null,
    IReadOnlyList<string>? SubtitleFormats = null,
    IReadOnlyList<string>? AccessModes = null,
    int? MaxHeight = null,
    IReadOnlyList<string>? Containers = null,
    int? MaxBitrateKbps = null)
{
    public static DeviceCapabilities Any { get; } = new();

    public bool CanPlayVideo(string? codec) => Allows(VideoCodecs, codec);

    public bool CanShowHdr(string format) => Allows(Hdr, format);

    public bool CanPassThrough(string format) => AudioPassthrough is not null && Contains(AudioPassthrough, format);

    public bool CanPlayAudio(string format) =>
        (AudioPassthrough is null && AudioDecode is null)
        || Contains(AudioPassthrough, format)
        || Contains(AudioDecode, format);

    public bool CanShowSubtitle(string format) => Allows(SubtitleFormats, format);

    public bool Accepts(string mode) => Allows(AccessModes, mode);

    public bool AcceptsOnly(string mode) => AccessModes is { Count: > 0 } && AccessModes.All(m => string.Equals(m, mode, StringComparison.OrdinalIgnoreCase));

    public bool CanOpenContainer(string? container) => Allows(Containers, container);

    private static bool Allows(IReadOnlyList<string>? list, string? value) => list is null || Contains(list, value);

    private static bool Contains(IReadOnlyList<string>? list, string? value) =>
        value is not null && list?.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) == true;
}
