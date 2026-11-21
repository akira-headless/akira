using MySqlConnector;
using Akira.Infrastructure.Configuration;
using Testcontainers.MariaDb;

namespace Akira.Testing;

/// <summary>
/// Runs one MariaDB container per test assembly and hands out a fresh database per test.
/// </summary>
public sealed class MariaDbContainerFixture : IAsyncDisposable
{
    public const string Image = "mariadb:11.4";

    // The image sets the root password to the configured user password
    private const string RootUser = "root";

    // Built and started on first use, not in the constructor: building already looks for Docker, and a run
    // filtered to the non-integration tests must not need it
    private readonly Lazy<Task<MariaDbContainer>> _container = new(async () =>
    {
        var container = new MariaDbBuilder(Image).Build();
        await container.StartAsync();
        return container;
    });

    public async ValueTask DisposeAsync()
    {
        if (_container.IsValueCreated)
        {
            await (await _container.Value).DisposeAsync();
        }
    }

    public async Task<MariaDbTestDatabase> CreateDatabaseAsync(
        CancellationToken cancellationToken,
        string characterSet = DatabasePreflight.RequiredMariaDbCharacterSet)
    {
        var container = await _container.Value.WaitAsync(cancellationToken);

        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.MariaDb,
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(MariaDbBuilder.MariaDbPort),
            Name = $"akira_test_{Guid.NewGuid():N}",
            Username = RootUser,
            Password = MariaDbBuilder.DefaultPassword
        };

        var adminConnectionString = new MySqlConnectionStringBuilder
        {
            Server = options.Host,
            Port = (uint)options.Port,
            UserID = RootUser,
            Password = options.Password
        }.ConnectionString;

        await using var connection = new MySqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE `{options.Name}` CHARACTER SET {characterSet}";
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new MariaDbTestDatabase(options, adminConnectionString);
    }
}
