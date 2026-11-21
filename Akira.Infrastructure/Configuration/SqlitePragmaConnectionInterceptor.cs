using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Akira.Infrastructure.Configuration;

public sealed class SqlitePragmaConnectionInterceptor : DbConnectionInterceptor
{
    public const int BusyTimeoutMilliseconds = 120000;

    // Makes a second writer wait for the lock instead of failing straight away with SQLITE_BUSY
    private static readonly string BusyTimeoutPragma = $"PRAGMA busy_timeout={BusyTimeoutMilliseconds}";

    // WAL lets API reads continue while a sync or probe job is writing
    private static readonly string[] WritePragmas =
    [
        "PRAGMA journal_mode=WAL",
        "PRAGMA synchronous=NORMAL"
    ];

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (connection is not SqliteConnection sqliteConnection)
        {
            return;
        }

        using var command = connection.CreateCommand();
        foreach (var pragma in GetPragmas(sqliteConnection))
        {
            command.CommandText = pragma;
            command.ExecuteNonQuery();
        }
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is not SqliteConnection sqliteConnection)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        foreach (var pragma in GetPragmas(sqliteConnection))
        {
            command.CommandText = pragma;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static IEnumerable<string> GetPragmas(SqliteConnection connection)
    {
        yield return BusyTimeoutPragma;

        // EF Core opens a read-only connection to check whether the database exists, and
        // switching the journal mode there fails when the file is not in WAL mode yet. WAL is
        // stored in the file itself, so the next writable connection sets it.
        if (new SqliteConnectionStringBuilder(connection.ConnectionString).Mode == SqliteOpenMode.ReadOnly)
        {
            yield break;
        }

        foreach (var pragma in WritePragmas)
        {
            yield return pragma;
        }
    }
}
