using FluentMigrator;

namespace Akira.Migrations.Migrations;

[Migration(19)]
public class M0019_AddArrLinkItemIndex : Migration
{
    // SQLite makes no index for a foreign key, and without this one every item scans all links to see whether an
    // instance still holds it. MariaDB made its own with the key, and would refuse to drop this one on the way down.
    public override void Up()
    {
        if (Schema.Table("arr_links").Index("ix_arr_links_media_item_id").Exists())
        {
            return;
        }

        IfDatabase(MigrationDatabase.Sqlite).Create.Index("ix_arr_links_media_item_id").OnTable("arr_links")
            .OnColumn("media_item_id").Ascending();
    }

    public override void Down()
    {
        IfDatabase(MigrationDatabase.Sqlite).Delete.Index("ix_arr_links_media_item_id").OnTable("arr_links");
    }
}
