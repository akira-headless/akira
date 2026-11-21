namespace Akira.Infrastructure.Updates;

public sealed class ClientUpdateSource
{
    public string ManifestUrl { get; set; } = "";

    // Sent as it is, so any host works: "Basic ..." for a Gitea package, "Bearer ..." for a token
    public string? Authorization { get; set; }
}
