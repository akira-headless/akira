using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Infrastructure.Configuration;
using Akira.Server.Interfaces.Services;
using Akira.Migrations;
using Akira.Server.Plugins;

namespace Akira.Server.Extensions;

public static class ApplicationBuilderExtensions
{
    // nonfile leaves paths with an extension to the static files, which only serve when no endpoint matched
    public const string WebUiRoute = "{*path:nonfile:regex(^(?!(api|openapi|scalar|bridges)(/|$)).*$)}";

    // The web UI loads only its own files and talks only to this server, its socket included ('self' covers ws and wss).
    // Vue sets bound styles through the style object, which a policy does not block. Scalar serves its own pages and
    // stays out of this, as do API answers.
    public const string WebUiContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; font-src 'self'; " +
        "object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

    public static async Task Configure(this WebApplication app)
    {
        var catalog = app.Services.GetRequiredService<PluginCatalog>();
        catalog.Log(app.Logger);
        await app.ApplyMigrations();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var setup = scope.ServiceProvider.GetRequiredService<ISetupService>();
            await setup.InitializeAsync(app.Lifetime.ApplicationStopping);
        }

        // Behind a trusted reverse proxy the request is what the proxy received: its scheme for the Secure cookie and the
        // urls handed out, the client's address for the rate limits. Nobody else's forwarded headers count.
        var proxies = app.Services.GetRequiredService<IOptions<ProxyOptions>>().Value.TrustedProxies;
        if (proxies.Count > 0)
        {
            var forwarded = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
            };
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
            foreach (var proxy in proxies)
            {
                if (IPAddress.TryParse(proxy, out var address))
                {
                    forwarded.KnownProxies.Add(address);
                }
                else
                {
                    forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(proxy));
                }
            }

            app.UseForwardedHeaders(forwarded);
        }

        // An answer is only ever what its content type says, also an image passed on from another server
        app.Use((context, next) =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return next(context);
        });

        // A bad request (an unparsable value, a string for a number) keeps its own status. Without
        // the selector the handler turned it into a 500 whenever binding throws, as it does in Development.
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
                ? badRequest.StatusCode
                : StatusCodes.Status500InternalServerError
        });
        app.UseStatusCodePages();

        // Pings keep the event socket alive through proxies and show a dead client within a minute
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

        // The web UI is built into wwwroot; its files are public, everything they show comes from the API
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = WebUiHeaders });

        app.UseMiddleware<SetupGateMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        // The API contract is public, the API behind it is not
        app.MapOpenApi().AllowAnonymous();
        if (app.Environment.IsDevelopment())
        {
            app.MapScalarApiReference().AllowAnonymous();
        }

        app.MapControllers();
        app.MapBridges();

        // Every other path is a page of the web UI, which routes itself. The API, the bridges, the OpenAPI document and Scalar stay
        // out, so an unknown API path is still a 404 problem and not a page.
        app.MapFallbackToFile(WebUiRoute, "index.html", new StaticFileOptions { OnPrepareResponse = WebUiHeaders }).AllowAnonymous();
    }

    // A page kept from before an upgrade names script files that are gone, so a browser always asks for the page
    // again. The files under assets carry a hash of their content in their name and never change.
    private static void WebUiHeaders(StaticFileResponseContext context)
    {
        var hashed = context.Context.Request.Path.StartsWithSegments("/assets");
        context.Context.Response.Headers.CacheControl = hashed ? "public, max-age=31536000, immutable" : "no-cache";
        context.Context.Response.Headers.ContentSecurityPolicy = WebUiContentSecurityPolicy;
    }

    private static async Task ApplyMigrations(this WebApplication app)
    {
        var database = app.Services.GetRequiredService<DatabaseConnection>();
        await DatabasePreflight.RunAsync(database, app.Lifetime.ApplicationStopping);

        app.Logger.LogInformation("Applying migrations to {DatabaseProvider} database {Database}",
            database.Provider, database.DisplayName);
        MigrationConfiguration.RunMigrations(app.Services);
    }
}
