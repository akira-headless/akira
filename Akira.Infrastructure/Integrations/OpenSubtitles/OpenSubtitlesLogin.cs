namespace Akira.Infrastructure.Integrations.OpenSubtitles;

/// <summary>
/// Represents OpenSubtitles' answer to a login, with the token for the account.
/// </summary>
internal sealed record OpenSubtitlesLogin(string? Token);
