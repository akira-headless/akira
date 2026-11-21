namespace Akira.Server.Models.Subtitles;

/// <summary>
/// Represents a subtitle track in the asked language that the version already has.
/// </summary>
public sealed record ExistingSubtitle(string OptionId, string StreamId, string? Title, string? Codec, bool Forced, bool HearingImpaired, bool External);
