using FluentMigrator;

namespace Akira.Migrations.Migrations;

[Migration(17)]
public class M0017_AddImageBlurHash : Migration
{
    public override void Up()
    {
        if (Schema.Table("media_images").Column("blur_hash").Exists())
        {
            return;
        }

        Alter.Table("media_images").AddColumn("blur_hash").AsCustom("VARCHAR(64)").Nullable();
    }

    public override void Down()
    {
        Delete.Column("blur_hash").FromTable("media_images");
    }
}
