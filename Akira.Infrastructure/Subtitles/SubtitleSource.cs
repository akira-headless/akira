namespace Akira.Infrastructure.Subtitles;

/// <summary>
/// Represents a subtitle track to convert. Path is the subtitle file itself when StreamIndex is null, otherwise the
/// video it is embedded in. Key names the conversions, the same for as long as the source is the same track. Language
/// is the track's, for text that is not UTF-8.
/// </summary>
public sealed record SubtitleSource(string Key, string Path, int? StreamIndex, string? Codec, string? Language = null);
