using MySqlConnector;
using Akira.Infrastructure.Configuration;
using Xunit;

namespace Akira.Infrastructure.Tests.Configuration;

public class DatabaseConnectionTests
{
    private static readonly string ContentRoot = Path.Combine(Path.GetTempPath(), "akira");

    [Fact]
    public void Sqlite_ResolvesRelativePathAgainstContentRoot()
    {
        var connection = DatabaseConnection.Create(new DatabaseOptions { SqlitePath = "config/akira.db" }, ContentRoot);

        Assert.Equal(DatabaseProvider.Sqlite, connection.Provider);
        Assert.Equal(Path.GetFullPath(Path.Combine(ContentRoot, "config", "akira.db")), connection.SqlitePath);
    }

    [Fact]
    public void Sqlite_KeepsRootedPath()
    {
        var rooted = Path.Combine(Path.GetTempPath(), "elsewhere", "akira.db");

        var connection = DatabaseConnection.Create(new DatabaseOptions { SqlitePath = rooted }, ContentRoot);

        Assert.Equal(rooted, connection.SqlitePath);
    }

    [Fact]
    public void Sqlite_EnablesForeignKeys()
    {
        var connection = DatabaseConnection.Create(new DatabaseOptions { SqlitePath = "akira.db" }, ContentRoot);

        Assert.Contains("Foreign Keys=True", connection.ConnectionString);
    }

    [Fact]
    public void MariaDb_BuildsConnectionStringFromSettings()
    {
        var connection = DatabaseConnection.Create(MariaDbOptions(), ContentRoot);
        var built = new MySqlConnectionStringBuilder(connection.ConnectionString);

        Assert.Equal(DatabaseProvider.MariaDb, connection.Provider);
        Assert.Equal("nas", built.Server);
        Assert.Equal(3307u, built.Port);
        Assert.Equal("akira", built.Database);
        Assert.Equal("akira", built.UserID);
        Assert.Equal("p@ss;word=1", built.Password);
        Assert.Null(connection.SqlitePath);
    }

    [Fact]
    public void MariaDb_DisplayNameLeavesOutCredentials()
    {
        var connection = DatabaseConnection.Create(MariaDbOptions(), ContentRoot);

        Assert.Equal("nas:3307/akira", connection.DisplayName);
        Assert.DoesNotContain("p@ss", connection.DisplayName);
    }

    private static DatabaseOptions MariaDbOptions() => new()
    {
        Provider = DatabaseProvider.MariaDb,
        Host = "nas",
        Port = 3307,
        Name = "akira",
        Username = "akira",
        Password = "p@ss;word=1"
    };
}
