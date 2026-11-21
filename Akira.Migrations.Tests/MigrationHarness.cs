using System.Reflection;
using FluentMigrator;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Processors;
using Microsoft.Extensions.DependencyInjection;
using Akira.Testing;

namespace Akira.Migrations.Tests;

public sealed class MigrationHarness : IDisposable
{
    private readonly TestDatabase _database;
    private readonly ServiceProvider _serviceProvider;

    public MigrationHarness(TestDatabase database)
    {
        _database = database;
        _serviceProvider = new ServiceCollection()
            .AddFluentMigrator(_ => database.Connection)
            .BuildServiceProvider();
    }

    public static IReadOnlyList<Type> MigrationTypes { get; } = typeof(MigrationConfiguration).Assembly
        .GetTypes()
        .Where(type => type.GetCustomAttribute<MigrationAttribute>() is not null)
        .ToList();

    public void MigrateUp() => MigrationConfiguration.RunMigrations(_serviceProvider);

    public void MigrateDown(long version)
    {
        using var scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateDown(version);
    }

    public IMigrationProcessor GetProcessor()
    {
        using var scope = _serviceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IProcessorAccessor>().Processor;
    }

    public async Task<List<long>> GetAppliedVersionsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM version_info ORDER BY version";

        var versions = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            versions.Add(reader.GetInt64(0));
        }

        return versions;
    }

    public void Dispose() => _serviceProvider.Dispose();
}
