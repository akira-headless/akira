using Akira.Core.Library;
using Akira.Infrastructure.Sync;

namespace Akira.Server.Playback;

public sealed class PlaybackOptions
{
    public const string SectionName = "Playback";

    // Stopping past this share of the duration marks the item played
    public double PlayedThreshold { get; set; } = 0.9;

    // Stopping before this is trying something, not watching it, so no resume point is kept
    public TimeSpan MinimumResume { get; set; } = TimeSpan.FromMinutes(1);

    // Server paths to the paths clients open directly, for example /data/media to nfs://nas/media
    public List<PathMappingOptions> ClientPathMappings { get; set; } = [];

    // The household's languages, for every user who did not set their own
    public LanguageDefaults Languages { get; set; } = new();

    public PathMapper CreateClientPathMapper() =>
        new(ClientPathMappings.Select(mapping => new PathMapping(mapping.From, mapping.To)));
}
