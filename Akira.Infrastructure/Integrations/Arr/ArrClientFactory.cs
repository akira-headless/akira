using Akira.Infrastructure.Integrations.Radarr;
using Akira.Infrastructure.Integrations.Sonarr;
using Akira.Infrastructure.Interfaces.Integrations;

namespace Akira.Infrastructure.Integrations.Arr;

public class ArrClientFactory : IArrClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ArrClientFactory(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public IRadarrClient CreateRadarr(ArrInstanceOptions instance)
    {
        if (instance.Kind != ArrKind.Radarr)
        {
            throw new ArgumentException($"Instance {instance.Key} is a {instance.Kind} instance.", nameof(instance));
        }

        return new RadarrClient(CreateHttpClient(instance), instance.DisplayName);
    }

    public ISonarrClient CreateSonarr(ArrInstanceOptions instance)
    {
        if (instance.Kind != ArrKind.Sonarr)
        {
            throw new ArgumentException($"Instance {instance.Key} is a {instance.Kind} instance.", nameof(instance));
        }

        return new SonarrClient(CreateHttpClient(instance), instance.DisplayName);
    }

    private HttpClient CreateHttpClient(ArrInstanceOptions instance)
    {
        var httpClient = _httpClientFactory.CreateClient(ArrHttpClientConfiguration.HttpClientName);
        ArrHttpClientConfiguration.Configure(httpClient, instance);
        return httpClient;
    }
}
