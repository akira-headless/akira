namespace Akira.Infrastructure.Integrations.OpenSubtitles;

/// <summary>
/// Represents OpenSubtitles' answer to a download, with the link to the file and the downloads that remain.
/// </summary>
internal sealed record OpenSubtitlesDownload(string? Link, int Remaining);
