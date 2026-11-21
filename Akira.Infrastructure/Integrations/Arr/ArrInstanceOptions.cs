using Akira.Core.Library;
using Akira.Infrastructure.Sync;

namespace Akira.Infrastructure.Integrations.Arr;

public sealed class ArrInstanceOptions
{
    // Stored on every link, file and image of the instance. Renaming it is the same as removing
    // the instance and adding a new one: its items are dropped and synced again under the new key.
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ArrKind Kind { get; set; }
    public string Url { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;

    // The password Radarr or Sonarr sends with its webhooks. Without one, webhooks of this instance are refused.
    public string? WebhookSecret { get; set; }

    // This instance's paths to the paths this server sees, for when they mount the share differently
    public List<PathMappingOptions> PathMappings { get; set; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Key : Name;

    public PathMapper CreatePathMapper() =>
        new(PathMappings.Select(mapping => new PathMapping(mapping.From, mapping.To)));

    // With path mappings a file has to fall under one; without them the instance sees the same paths as this server
    public LibraryPaths CreateLibraryPaths(IReadOnlyList<string> protectedFolders) =>
        new(CreatePathMapper(), PathMappings.Count > 0, protectedFolders);
}
