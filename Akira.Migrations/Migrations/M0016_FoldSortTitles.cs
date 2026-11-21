using System.Data;
using FluentMigrator;
using Akira.Core.Library;

namespace Akira.Migrations.Migrations;

[Migration(16)]
public class M0016_FoldSortTitles : Migration
{
    public override void Up()
    {
        // Lists sort folded titles from now on (the database context folds every save); the rows that exist are folded here
        IfDatabase(MigrationDatabase.Sqlite).Execute.WithConnection(Fold);
        IfDatabase(MigrationDatabase.MariaDb).Execute.WithConnection(Fold);
    }

    // Folding loses the accents for good; the next sync writes every sort title again from Radarr and Sonarr
    public override void Down()
    {
    }

    private static void Fold(IDbConnection connection, IDbTransaction transaction)
    {
        var rows = new List<(object Id, string SortTitle)>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT id, sort_title FROM media_items";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetValue(0), reader.GetString(1)));
            }
        }

        foreach (var (id, sortTitle) in rows)
        {
            var folded = SearchText.Fold(sortTitle);
            if (folded == sortTitle)
            {
                continue;
            }

            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE media_items SET sort_title = @title WHERE id = @id";
            Add(update, "@title", folded);
            Add(update, "@id", id);
            update.ExecuteNonQuery();
        }
    }

    private static void Add(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
