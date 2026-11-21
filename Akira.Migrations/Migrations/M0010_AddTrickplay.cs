using System.Data;
using FluentMigrator;

namespace Akira.Migrations.Migrations;

[Migration(10)]
public class M0010_AddTrickplay : GuardedMigration
{
    public override void Up()
    {
        if (!Schema.Table("trickplay").Exists())
        {
            Create.Table("trickplay")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("media_file_id").AsGuid().NotNullable()
                    .ForeignKey("fk_trickplay_media_file_id", "media_files", "id").OnDelete(Rule.Cascade)
                .WithColumn("status").AsCustom("VARCHAR(20)").NotNullable()
                .WithColumn("version").AsInt32().NotNullable()
                .WithColumn("interval_ms").AsInt32().NotNullable()
                .WithColumn("width").AsInt32().NotNullable()
                .WithColumn("height").AsInt32().NotNullable()
                .WithColumn("tile_columns").AsInt32().NotNullable()
                .WithColumn("tile_rows").AsInt32().NotNullable()
                .WithColumn("thumbnail_count").AsInt32().NotNullable()
                .WithColumn("error").AsCustom("TEXT").Nullable()
                .WithColumn("generated_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_trickplay_media_file_id", "trickplay", "media_file_id", "media_files");
        }

        if (!Schema.Table("trickplay").Index("ix_trickplay_media_file_id").Exists())
        {
            Create.Index("ix_trickplay_media_file_id").OnTable("trickplay").OnColumn("media_file_id").Ascending().WithOptions().Unique();
        }
    }

    public override void Down()
    {
        Delete.Table("trickplay");
    }
}
