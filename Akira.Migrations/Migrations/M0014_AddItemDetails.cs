using FluentMigrator;

namespace Akira.Migrations.Migrations;

[Migration(14)]
public class M0014_AddItemDetails : Migration
{
    private static readonly (string Name, System.Action<FluentMigrator.Builders.Alter.Table.IAlterTableColumnAsTypeSyntax> Type)[] Columns =
    [
        ("imdb_rating", column => column.AsDouble().Nullable()),
        ("imdb_votes", column => column.AsInt32().Nullable()),
        ("tmdb_rating", column => column.AsDouble().Nullable()),
        ("rating", column => column.AsDouble().Nullable()),
        ("trailer_youtube_id", column => column.AsCustom("VARCHAR(32)").Nullable()),
        ("genres", column => column.AsCustom("VARCHAR(500)").Nullable()),
        ("certification", column => column.AsCustom("VARCHAR(20)").Nullable()),
    ];

    public override void Up()
    {
        foreach (var (name, type) in Columns)
        {
            if (!Schema.Table("media_items").Column(name).Exists())
            {
                type(Alter.Table("media_items").AddColumn(name));
            }
        }
    }

    public override void Down()
    {
        foreach (var (name, _) in Columns)
        {
            Delete.Column(name).FromTable("media_items");
        }
    }
}
