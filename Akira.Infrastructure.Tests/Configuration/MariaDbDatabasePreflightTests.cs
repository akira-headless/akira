using Akira.Infrastructure.Configuration;
using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Configuration;

[Trait("Category", "Integration")]
public class MariaDbDatabasePreflightTests(MariaDbContainerFixture mariaDb)
{
    [Fact]
    public async Task Utf8Mb4Database_Passes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await mariaDb.CreateDatabaseAsync(cancellationToken);

        await DatabasePreflight.RunAsync(database.Connection, cancellationToken);

        Assert.True(database.Connection.ServerVersion.Version.Major >= 10);
    }

    [Theory]
    [InlineData("latin1")]
    [InlineData("utf8mb3")]
    public async Task OtherCharacterSet_FailsWithFix(string characterSet)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await mariaDb.CreateDatabaseAsync(cancellationToken, characterSet);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DatabasePreflight.RunAsync(database.Connection, cancellationToken));

        Assert.Contains($"'{characterSet}'", exception.Message);
        Assert.Contains("ALTER DATABASE", exception.Message);
    }
}
