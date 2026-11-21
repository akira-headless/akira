using Akira.Contracts.Bridges;
using Akira.Server.Authentication;
using Akira.Server.Bridges;
using Akira.Server.Plugins;

namespace Akira.Server.Endpoints;

public static class BridgeEndpoints
{
    /// <summary>
    /// Keeps a bridge found on the network to the local network, and logs where each switched on bridge answers. Their
    /// controllers are mapped with the API's and kept under /bridges/{key} by BridgeControllerConvention.
    /// </summary>
    public static void MapBridges(this WebApplication app)
    {
        var catalog = app.Services.GetRequiredService<PluginCatalog>();
        foreach (var (key, bridge) in catalog.Bridges(app.Services))
        {
            var path = new PathString($"{BridgeService.BasePath}/{key}");
            if (bridge.Connect.Kind == ConnectKind.Discovery)
            {
                // Found on the network and without sign in, so it answers the network only: never the internet, never
                // through a proxy, never under a name
                app.UseWhen(context => context.Request.Path.StartsWithSegments(path) && !ClientAddresses.IsLocal(context),
                    branch => branch.Run(context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }));
            }

            app.Logger.LogInformation("Bridge {Bridge} answers at {Path}", key, path.Value);
        }
    }
}
