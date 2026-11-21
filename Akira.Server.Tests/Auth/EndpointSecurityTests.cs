using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Akira.Server.Testing;
using Xunit;

namespace Akira.Server.Tests.Auth;

public class EndpointSecurityTests
{
    // Every other endpoint needs credentials through the fallback policy. Opening one more is a
    // decision, so it has to be added here on purpose.
    private static readonly string[] Anonymous =
    [
        "GET /api/v1/health",
        "GET /api/v1/setup",
        "POST /api/v1/setup",
        "POST /api/v1/auth/pairings",
        "POST /api/v1/auth/pairings/{id:guid}/token",

        // At home only, refused through a proxy Akira does not trust; the PIN is the credential
        "GET /api/v1/auth/pin/users",
        "POST /api/v1/auth/pin/sign-in",

        // Radarr and Sonarr cannot hold a device token, the instance's webhook secret is checked instead
        "POST /api/v1/webhooks/{instanceKey}",
        "GET /openapi/{documentName}.json",

        // A signed url is its own credential, made by a bridge for a client that cannot send one
        "GET /api/v1/signed/{token}/{name}",
        "HEAD /api/v1/signed/{token}/{name}",

        // The pages of the web UI; what they show comes from the API
        "GET /" + Akira.Server.Extensions.ApplicationBuilderExtensions.WebUiRoute,
        "HEAD /" + Akira.Server.Extensions.ApplicationBuilderExtensions.WebUiRoute,
        "GET /scalar/{documentName?}"
    ];

    [Fact]
    public async Task OnlyTheListedEndpoints_AllowAnonymousAccess()
    {
        await using var factory = new AkiraApiFactory();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

        var anonymous = endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(method => $"{method} /{endpoint.RoutePattern.RawText!.Trim('/')}"))
            .Where(route => !route.Contains("/scalar/") || route.StartsWith("GET /scalar/{documentName?}"))
            .Distinct()
            .Order()
            .ToList();

        Assert.Equal(Anonymous.Order(), anonymous);
    }

    [Fact]
    public async Task OpenApiDocument_IsPublic_AndDeclaresBothCredentials()
    {
        await using var factory = new AkiraApiFactory();
        using var anonymous = factory.CreateAnonymousClient();

        var response = await anonymous.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var schemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("bearer", schemes.GetProperty("deviceToken").GetProperty("scheme").GetString());
        Assert.Equal("X-Api-Key", schemes.GetProperty("apiKey").GetProperty("name").GetString());
    }
}
