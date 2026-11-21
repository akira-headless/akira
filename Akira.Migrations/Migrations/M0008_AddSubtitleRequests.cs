using System.Data;
using FluentMigrator;

namespace Akira.Migrations.Migrations;

[Migration(8)]
public class M0008_AddSubtitleRequests : GuardedMigration
{
    public override void Up()
    {
        if (!Schema.Table("subtitle_requests").Exists())
        {
            Create.Table("subtitle_requests")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("user_id").AsGuid().NotNullable()
                    .ForeignKey("fk_subtitle_requests_user_id", "users", "id").OnDelete(Rule.Cascade)
                .WithColumn("media_item_id").AsGuid().NotNullable()
                    .ForeignKey("fk_subtitle_requests_media_item_id", "media_items", "id").OnDelete(Rule.Cascade)
                .WithColumn("media_file_id").AsGuid().NotNullable()
                    .ForeignKey("fk_subtitle_requests_media_file_id", "media_files", "id").OnDelete(Rule.Cascade)
                .WithColumn("language").AsCustom("VARCHAR(3)").NotNullable()
                .WithColumn("kind").AsCustom("VARCHAR(20)").NotNullable()
                .WithColumn("provider_key").AsCustom("VARCHAR(32)").NotNullable()
                .WithColumn("source_reference").AsCustom("VARCHAR(100)").Nullable()
                .WithColumn("external_job_id").AsCustom("VARCHAR(100)").Nullable()
                .WithColumn("status").AsCustom("VARCHAR(20)").NotNullable()
                .WithColumn("progress").AsInt32().NotNullable()
                .WithColumn("result_stream_id").AsCustom("VARCHAR(32)").Nullable()
                .WithColumn("result_path").AsCustom("TEXT").Nullable()
                .WithColumn("error").AsCustom("TEXT").Nullable()
                .WithColumn("temporary_path").AsCustom("TEXT").Nullable()
                .WithColumn("session_id").AsGuid().Nullable()
                .WithColumn("completed_at").AsCustom("DATETIME(6)").Nullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_subtitle_requests_user_id", "subtitle_requests", "user_id", "users");
            AddForeignKeyIfMissing("fk_subtitle_requests_media_item_id", "subtitle_requests", "media_item_id", "media_items");
            AddForeignKeyIfMissing("fk_subtitle_requests_media_file_id", "subtitle_requests", "media_file_id", "media_files");
        }

        if (!Schema.Table("subtitle_requests").Index("ix_subtitle_requests_status").Exists())
        {
            Create.Index("ix_subtitle_requests_status").OnTable("subtitle_requests").OnColumn("status").Ascending();
        }

        if (!Schema.Table("subtitle_requests").Index("ix_subtitle_requests_media_file_id_language").Exists())
        {
            Create.Index("ix_subtitle_requests_media_file_id_language").OnTable("subtitle_requests")
                .OnColumn("media_file_id").Ascending()
                .OnColumn("language").Ascending();
        }

        if (!Schema.Table("subtitle_requests").Index("ix_subtitle_requests_user_id").Exists())
        {
            Create.Index("ix_subtitle_requests_user_id").OnTable("subtitle_requests").OnColumn("user_id").Ascending();
        }

        if (!Schema.Table("subtitle_requests").Index("ix_subtitle_requests_media_item_id").Exists())
        {
            Create.Index("ix_subtitle_requests_media_item_id").OnTable("subtitle_requests").OnColumn("media_item_id").Ascending();
        }
    }

    public override void Down()
    {
        Delete.Table("subtitle_requests");
    }
}
