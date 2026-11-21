namespace Akira.Infrastructure.Integrations.Trakt;

// The API app this server is registered as on trakt.tv (Settings, Your API Apps). Each user links their own account to it.
public sealed class TraktOptions
{
    public const string SectionName = "Trakt";

    public string Url { get; set; } = "https://api.trakt.tv";
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
