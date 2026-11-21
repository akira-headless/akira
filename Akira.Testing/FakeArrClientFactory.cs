using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Interfaces.Integrations;

namespace Akira.Testing;

public sealed class FakeArrClientFactory : IArrClientFactory
{
    public Dictionary<string, FakeRadarrClient> Radarr { get; } = [];
    public Dictionary<string, FakeSonarrClient> Sonarr { get; } = [];

    public IRadarrClient CreateRadarr(ArrInstanceOptions instance) => Radarr[instance.Key];

    public ISonarrClient CreateSonarr(ArrInstanceOptions instance) => Sonarr[instance.Key];

    public static ArrInstanceOptions Instance(string key, ArrKind kind, params (string From, string To)[] mappings) => new()
    {
        Key = key,
        Name = key,
        Kind = kind,
        Url = $"http://{key}:7878",
        ApiKey = "key",
        PathMappings = mappings.Select(mapping => new Akira.Infrastructure.Sync.PathMappingOptions
        {
            From = mapping.From,
            To = mapping.To
        }).ToList()
    };
}
