namespace Akira.Contracts.Library;

/// <summary>
/// Represents an audio or subtitle track of a file. StreamId stays the same across probes, Language is ISO 639-2/B and
/// Order is the track's place in the container, null for a file next to the video. Never store Order, it changes when
/// the file does.
/// </summary>
public sealed record MediaTrack(
    string StreamId,
    string? Language,
    string? Codec,
    int? Channels,
    string? Title,
    bool Default,
    bool Forced,
    bool HearingImpaired,
    bool External,
    bool TextBased,
    int? Order = null,
    string? Profile = null,
    string? ChannelLayout = null,
    bool Commentary = false);
