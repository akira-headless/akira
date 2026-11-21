using Akira.Core.Enum;

namespace Akira.Core.Entities;

public class TrackPreference : BaseEntity
{
    public const string GlobalKey = "global";

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public PreferenceScope Scope { get; set; }

    // The series or item IdentityKey, or GlobalKey. Never null, so the unique index treats every
    // scope the same on both engines.
    public required string ScopeKey { get; set; }

    public string? AudioLanguages { get; set; }
    public bool? PreferOriginalLanguage { get; set; }
    public bool? PreferLossless { get; set; }
    public bool? ExcludeCommentary { get; set; }
    public bool? ExcludeDescriptive { get; set; }
    public SubtitleMode? SubtitleMode { get; set; }
    public string? SubtitleLanguages { get; set; }
    public bool? PreferHearingImpaired { get; set; }

    public string? RememberedAudio { get; set; }
    public string? RememberedSubtitle { get; set; }
    public bool? RememberedSubtitleOff { get; set; }
}
