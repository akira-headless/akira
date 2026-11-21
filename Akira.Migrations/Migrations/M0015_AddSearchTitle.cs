using System.Data;
using FluentMigrator;
using Akira.Core.Library;

namespace Akira.Migrations.Migrations;

[Migration(15)]
public class M0015_AddSearchTitle : Migration
{
    public override void Up()
    {
        if (!Schema.Table("media_items").Column("search_title").Exists())
        {
            Alter.Table("media_items").AddColumn("search_title").AsCustom("TEXT").Nullable();
        }

        // Folding needs .NET, not SQL, so the rows that exist get it here; every later save sets it.
        // Only rows without one, so a fill that failed after the column was committed is done on the next run.
        IfDatabase(MigrationDatabase.Sqlite).Execute.WithConnection(Fill);
        IfDatabase(MigrationDatabase.MariaDb).Execute.WithConnection(Fill);
    }

    public override void Down()
    {
        Delete.Column("search_title").FromTable("media_items");
    }

    private static void Fill(IDbConnection connection, IDbTransaction transaction)
    {
        var rows = new List<(object Id, string Title)>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT id, title FROM media_items WHERE search_title IS NULL";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetValue(0), reader.GetString(1)));
            }
        }

        foreach (var (id, title) in rows)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE media_items SET search_title = @title WHERE id = @id";
            Add(update, "@title", SearchText.Fold(title));
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
