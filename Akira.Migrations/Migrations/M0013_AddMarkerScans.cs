using System.Data;
using FluentMigrator;

namespace Akira.Migrations.Migrations;

[Migration(13)]
public class M0013_AddMarkerScans : GuardedMigration
{
    public override void Up()
    {
        if (!Schema.Table("marker_scans").Exists())
        {
            Create.Table("marker_scans")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("media_file_id").AsGuid().NotNullable()
                    .ForeignKey("fk_marker_scans_media_file_id", "media_files", "id").OnDelete(Rule.Cascade)
                .WithColumn("version").AsInt32().NotNullable()
                .WithColumn("intro_start_ms").AsInt64().Nullable()
                .WithColumn("intro_end_ms").AsInt64().Nullable()
                .WithColumn("credits_start_ms").AsInt64().Nullable()
                .WithColumn("credits_end_ms").AsInt64().Nullable()
                .WithColumn("error").AsCustom("TEXT").Nullable()
                .WithColumn("scanned_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_marker_scans_media_file_id", "marker_scans", "media_file_id", "media_files");
        }

        if (!Schema.Table("marker_scans").Index("ix_marker_scans_media_file_id").Exists())
        {
            Create.Index("ix_marker_scans_media_file_id").OnTable("marker_scans").OnColumn("media_file_id").Ascending().WithOptions().Unique();
        }
    }

    public override void Down()
    {
        Delete.Table("marker_scans");
    }
}
