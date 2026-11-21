using Akira.Core.Enum;

namespace Akira.Core.Playback;

/// <summary>
/// Represents a track a user picked before, by its signature, and the scope it was saved at. Off means subtitles were
/// turned off, with no signature.
/// </summary>
public sealed record Remembered(string? Signature, bool Off, PreferenceScope Scope);
