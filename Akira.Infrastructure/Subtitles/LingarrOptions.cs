using Akira.Core.Library;
using Akira.Infrastructure.Sync;

namespace Akira.Infrastructure.Subtitles;

public sealed class LingarrOptions
{
    public string? Url { get; set; }

    // Only needed when Lingarr has authentication on, without it Lingarr lets every request through
    public string? ApiKey { get; set; }

    // This server's paths to the paths Lingarr sees, when they mount the share differently
    public List<PathMappingOptions> PathMappings { get; set; } = [];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url);

    public PathMapper ToLingarr() => new(PathMappings.Select(mapping => new PathMapping(mapping.From, mapping.To)));

    public PathMapper FromLingarr() => new(PathMappings.Select(mapping => new PathMapping(mapping.To, mapping.From)));
}
