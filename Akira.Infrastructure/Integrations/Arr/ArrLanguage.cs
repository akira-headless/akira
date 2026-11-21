namespace Akira.Infrastructure.Integrations.Arr;

/// <summary>
/// Represents a language as Radarr and Sonarr return it, by their own id and English name.
/// </summary>
public sealed record ArrLanguage(int Id, string? Name);
