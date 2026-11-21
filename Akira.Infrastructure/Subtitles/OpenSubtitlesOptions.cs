namespace Akira.Infrastructure.Subtitles;

public sealed class OpenSubtitlesOptions
{
    public string Url { get; set; } = "https://api.opensubtitles.com/api/v1";
    public string? ApiKey { get; set; }

    // Downloads count against an account. Without one, OpenSubtitles allows only a few a day per address.
    public string? Username { get; set; }
    public string? Password { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
