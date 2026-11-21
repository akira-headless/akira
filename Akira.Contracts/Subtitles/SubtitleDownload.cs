namespace Akira.Contracts.Subtitles;

/// <summary>
/// Represents the bytes of a downloaded subtitle and its format as a file extension.
/// </summary>
public sealed record SubtitleDownload(byte[] Content, string Format);
