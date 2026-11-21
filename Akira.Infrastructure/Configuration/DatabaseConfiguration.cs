using Microsoft.EntityFrameworkCore;

namespace Akira.Infrastructure.Configuration;

public static class DatabaseConfiguration
{
    private static readonly SqlitePragmaConnectionInterceptor SqlitePragmas = new();

    public static DbContextOptionsBuilder ConfigureDbContext(DbContextOptionsBuilder options, DatabaseConnection connection)
    {
        switch (connection.Provider)
        {
            case DatabaseProvider.Sqlite:
                options
                    .UseSqlite(connection.ConnectionString,
                        sqliteOptions => sqliteOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                    .AddInterceptors(SqlitePragmas);
                break;
            case DatabaseProvider.MariaDb:
                options.UseMySql(connection.ConnectionString, connection.ServerVersion,
                    mariaDbOptions => mariaDbOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
                break;
            default:
                throw new InvalidOperationException($"Database provider '{connection.Provider}' is not supported.");
        }

        return options.UseSnakeCaseNamingConvention();
    }
}
