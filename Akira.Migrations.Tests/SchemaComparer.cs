using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Akira.Testing;

namespace Akira.Migrations.Tests;

/// <summary>
/// Compares an EF Core model against the schema that actually exists in a database.
/// </summary>
public static partial class SchemaComparer
{
    public static async Task<List<string>> FindMissingAsync(
        DbContext context,
        TestDatabase database,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        foreach (var (table, properties) in GetMappedTables(context))
        {
            var columns = (await database.GetColumnsAsync(table.Name, cancellationToken))
                .Select(column => column.Name)
                .ToHashSet();

            if (columns.Count == 0)
            {
                problems.Add($"Table {table.Name} is mapped but does not exist.");
                continue;
            }

            problems.AddRange(properties
                .Select(property => property.GetColumnName(table)!)
                .Where(column => !columns.Contains(column))
                .Select(column => $"Column {table.Name}.{column} is mapped but does not exist."));
        }

        return problems;
    }

    public static async Task<List<string>> FindTypeMismatchesAsync(
        DbContext context,
        TestDatabase database,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        foreach (var (table, properties) in GetMappedTables(context))
        {
            var columns = (await database.GetColumnsAsync(table.Name, cancellationToken))
                .ToDictionary(column => column.Name);

            foreach (var property in properties)
            {
                var name = property.GetColumnName(table)!;
                if (!columns.TryGetValue(name, out var column))
                {
                    continue;
                }

                var expected = NormalizeColumnType(property.GetColumnType(table));
                var actual = NormalizeColumnType(column.Type);
                if (expected != actual)
                {
                    problems.Add($"Column {table.Name}.{name} is {actual} in the database but {expected} in the EF model.");
                }
            }
        }

        return problems;
    }

    public static async Task<List<string>> FindNonUtf8Mb4ColumnsAsync(
        TestDatabase database,
        IReadOnlyCollection<string> ignoredTables,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT table_name, column_name, character_set_name
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND character_set_name IS NOT NULL
              AND character_set_name <> 'utf8mb4'
            ORDER BY table_name, ordinal_position
            """;

        var problems = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var table = reader.GetString(0);
            if (!ignoredTables.Contains(table))
            {
                problems.Add($"Column {table}.{reader.GetString(1)} is {reader.GetString(2)}, not utf8mb4.");
            }
        }

        return problems;
    }

    // MariaDB reports a display width for integer types and FluentMigrator writes INTEGER, neither
    // changes what the column stores. tinyint(1) keeps its width, it is how a boolean is mapped.
    public static string NormalizeColumnType(string? columnType)
    {
        var normalized = (columnType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized == "integer")
        {
            return "int";
        }

        return IntegerDisplayWidth().Replace(normalized, "$1");
    }

    private static IEnumerable<(StoreObjectIdentifier Table, IReadOnlyList<IProperty> Properties)> GetMappedTables(
        DbContext context)
    {
        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName is null)
            {
                continue;
            }

            var table = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
            var properties = entityType.GetProperties()
                .Where(property => property.GetColumnName(table) is not null)
                .ToList();

            yield return (table, properties);
        }
    }

    [GeneratedRegex(@"^(smallint|mediumint|int|bigint)\(\d+\)")]
    private static partial Regex IntegerDisplayWidth();
}
