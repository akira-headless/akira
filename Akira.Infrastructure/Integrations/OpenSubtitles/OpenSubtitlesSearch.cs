namespace Akira.Infrastructure.Integrations.OpenSubtitles;

/// <summary>
/// Represents OpenSubtitles' answer to a subtitle search.
/// </summary>
internal sealed record OpenSubtitlesSearch(List<OpenSubtitlesResult>? Data);
