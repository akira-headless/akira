using Microsoft.Extensions.DependencyInjection;

namespace Akira.Infrastructure.Integrations;

internal static class SubtitleHttpClients
{
    // A subtitle is a few hundred KB; a source or translator taking longer than this is not coming back
    public static void AddSubtitleHttpClient(this IServiceCollection services, string name) =>
        services.AddHttpClient(name, httpClient => httpClient.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(UpstreamHttp.CreateHandler);
}
