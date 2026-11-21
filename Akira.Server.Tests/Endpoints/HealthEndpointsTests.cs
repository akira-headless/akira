using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Akira.Core;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Health;
using Akira.Server.Testing;
using Xunit;

namespace Akira.Server.Tests.Endpoints;

public class HealthEndpointsTests
{
    [Fact]
    public async Task GetHealth_ReturnsHealthyWithVersion()
    {
        await using var factory = new AkiraApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/health", TestContext.Current.CancellationToken);
        using var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("healthy", body.RootElement.GetProperty("database").GetString());
        Assert.Equal(ServerVersion.Number, body.RootElement.GetProperty("version").GetString());

        // The probe worker never ran in this test host, so ffprobe has not been checked
        Assert.Equal("unknown", body.RootElement.GetProperty("ffprobe").GetString());
    }

    [Fact]
    public async Task GetHealth_ReturnsServiceUnavailable_WhenDatabaseIsUnreachable()
    {
        var healthService = new Mock<IHealthService>();
        healthService
            .Setup(service => service.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthResponse(HealthState.Unhealthy, ServerVersion.Number, HealthState.Unhealthy, HealthState.Unknown));

        await using var factory = new AkiraApiFactory();
        using var client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddScoped(_ => healthService.Object)))
            .CreateClient();

        var response = await client.GetAsync("/api/v1/health", TestContext.Current.CancellationToken);
        using var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("unhealthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("unhealthy", body.RootElement.GetProperty("database").GetString());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
