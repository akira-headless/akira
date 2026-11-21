namespace Akira.Infrastructure.Integrations.Arr;

public static class ArrHttpClientConfiguration
{
    public const string HttpClientName = "arr";

    // Sonarr builds the episode list of a long running series on request, which can take a while
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public static void Configure(HttpClient httpClient, ArrInstanceOptions instance)
    {
        httpClient.Timeout = Timeout;

        // The trailing slash keeps a url base like /radarr when relative paths are resolved
        httpClient.BaseAddress = new Uri(instance.Url.TrimEnd('/') + "/");
        httpClient.DefaultRequestHeaders.Add("X-Api-Key", instance.ApiKey);
    }
}
