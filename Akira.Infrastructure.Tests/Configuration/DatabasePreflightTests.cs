using Akira.Infrastructure.Configuration;
using Xunit;

namespace Akira.Infrastructure.Tests.Configuration;

public class DatabasePreflightTests
{
    [Fact]
    public async Task Sqlite_CreatesMissingDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"akira_preflight_{Guid.NewGuid():N}");
        var connection = DatabaseConnection.Create(
            new DatabaseOptions { SqlitePath = Path.Combine(directory, "config", "akira.db") },
            Path.GetTempPath());

        try
        {
            await DatabasePreflight.RunAsync(connection, TestContext.Current.CancellationToken);

            Assert.True(Directory.Exists(Path.Combine(directory, "config")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
