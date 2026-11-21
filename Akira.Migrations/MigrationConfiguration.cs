using FluentMigrator.Runner;
using FluentMigrator.Runner.Processors;
using Microsoft.Extensions.DependencyInjection;
using Akira.Infrastructure.Configuration;

namespace Akira.Migrations;

public static class MigrationConfiguration
{
    public static IServiceCollection AddFluentMigrator(
        this IServiceCollection services,
        Func<IServiceProvider, DatabaseConnection> connection)
    {
        services.AddLogging();
        services.AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddSQLite()
                .AddMySql5()
                .WithVersionTable(new VersionTableMetaData())
                .ScanIn(typeof(MigrationConfiguration).Assembly).For.Migrations());

        // Resolved lazily so the connection follows configuration overrides, the same way the
        // DbContext does. Both runners are registered, ProcessorId picks the one that runs.
        services.AddOptions<ProcessorOptions>()
            .Configure<IServiceProvider>((options, provider) =>
                options.ConnectionString = connection(provider).ConnectionString);
        services.AddOptions<SelectingProcessorAccessorOptions>()
            .Configure<IServiceProvider>((options, provider) =>
                options.ProcessorId = GetProcessorId(connection(provider).Provider));

        return services;
    }

    public static string GetProcessorId(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.Sqlite => "SQLite",
            DatabaseProvider.MariaDb => "MySql5",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "No migration processor for this provider.")
        };
    }

    public static void RunMigrations(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
