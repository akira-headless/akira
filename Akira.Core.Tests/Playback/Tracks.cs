using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Playback;

namespace Akira.Core.Tests.Playback;

/// <summary>
/// Builds track layouts for resolver tests.
/// </summary>
public sealed class Tracks
{
    private readonly List<MediaStream> _streams = [];

    public IReadOnlyList<MediaStream> All => _streams;

    public MediaStream this[string id] => _streams.Single(s => s.Id == id);

    public Tracks Audio(string id, string? language, string codec, int channels, string? title = null, bool isDefault = false,
        bool commentary = false, bool descriptive = false, string? profile = null)
    {
        _streams.Add(new MediaStream
        {
            Id = id, Kind = StreamKind.Audio, SortOrder = _streams.Count, Language = language, Codec = codec, Channels = channels,
            Title = title, IsDefault = isDefault, IsCommentary = commentary, IsDescriptive = descriptive, Profile = profile
        });
        return this;
    }

    public Tracks Subtitle(string id, string? language, string codec, int? events = null, string? title = null, bool forced = false,
        bool hearingImpaired = false, bool isDefault = false, bool external = false)
    {
        _streams.Add(new MediaStream
        {
            Id = id, Kind = StreamKind.Subtitle, SortOrder = _streams.Count, Language = language, Codec = codec, EventCount = events,
            Title = title, IsForced = forced, IsHearingImpaired = hearingImpaired, IsDefault = isDefault, IsExternal = external,
            IsTextBased = codec is "subrip" or "ass" or "mov_text" or "webvtt"
        });
        return this;
    }

    public TrackSelection Resolve(TrackRules? global = null, DeviceCapabilities? capabilities = null, string? original = null,
        TrackRules? series = null, TrackRules? item = null) =>
        TrackResolver.Resolve(_streams, original,
            EffectiveTrackRules.Combine(item ?? TrackRules.None, series ?? TrackRules.None, global ?? TrackRules.None),
            capabilities ?? DeviceCapabilities.Any);
}
