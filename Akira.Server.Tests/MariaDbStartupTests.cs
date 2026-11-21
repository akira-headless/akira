using System.Net;
using System.Text.Json;
using Akira.Testing;
using Akira.Server.Testing;
using Xunit;

namespace Akira.Server.Tests;

[Trait("Category", "Integration")]
public class MariaDbStartupTests(MariaDbContainerFixture mariaDb)
{
    [Fact]
    public async Task MariaDb_StartsMigratesAndReportsHealthy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await mariaDb.CreateDatabaseAsync(cancellationToken);
        await using var factory = new AkiraApiFactory(database.Options);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/health", cancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", body.RootElement.GetProperty("database").GetString());
        Assert.NotEmpty(await database.GetColumnsAsync("version_info", cancellationToken));
    }
}
