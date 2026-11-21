using Akira.Infrastructure.Integrations.Arr;

namespace Akira.Infrastructure.Interfaces.Integrations;

public interface IArrClientFactory
{
    IRadarrClient CreateRadarr(ArrInstanceOptions instance);

    ISonarrClient CreateSonarr(ArrInstanceOptions instance);
}
