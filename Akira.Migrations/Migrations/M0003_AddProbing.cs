using System.Data;
using FluentMigrator;
using FluentMigrator.Builders.Alter.Table;

namespace Akira.Migrations.Migrations;

[Migration(3)]
public class M0003_AddProbing : GuardedMigration
{
    private static readonly (string Name, Action<IAlterTableColumnAsTypeSyntax> Type)[] FileColumns =
    [
        ("duration_ms", column => column.AsInt64().Nullable()),
        ("probe_status", column => column.AsCustom("VARCHAR(20)").NotNullable().WithDefaultValue("Pending")),
        ("probe_error", column => column.AsCustom("TEXT").Nullable()),
        ("probed_at", column => column.AsCustom("DATETIME(6)").Nullable()),
        ("probe_version", column => column.AsInt32().Nullable()),
        ("probed_size_bytes", column => column.AsInt64().Nullable()),
        ("probed_modified_at", column => column.AsCustom("DATETIME(6)").Nullable()),
    ];

    public override void Up()
    {
        foreach (var (name, type) in FileColumns)
        {
            if (!Schema.Table("media_files").Column(name).Exists())
            {
                type(Alter.Table("media_files").AddColumn(name));
            }
        }

        if (!Schema.Table("media_files").Index("ix_media_files_probe_status").Exists())
        {
            Create.Index("ix_media_files_probe_status").OnTable("media_files")
                .OnColumn("probe_status").Ascending();
        }

        if (!Schema.Table("media_streams").Exists())
        {
            Create.Table("media_streams")
                .WithColumn("id").AsCustom("VARCHAR(32)").PrimaryKey()
                .WithColumn("media_file_id").AsGuid().NotNullable()
                    .ForeignKey("fk_media_streams_media_file_id", "media_files", "id").OnDelete(Rule.Cascade)
                .WithColumn("kind").AsCustom("VARCHAR(20)").NotNullable()
                .WithColumn("sort_order").AsInt32().NotNullable()
                .WithColumn("codec").AsCustom("VARCHAR(50)").Nullable()
                .WithColumn("profile").AsCustom("VARCHAR(100)").Nullable()
                .WithColumn("language").AsCustom("VARCHAR(3)").Nullable()
                .WithColumn("title").AsCustom("TEXT").Nullable()
                .WithColumn("channels").AsInt32().Nullable()
                .WithColumn("channel_layout").AsCustom("VARCHAR(50)").Nullable()
                .WithColumn("bit_depth").AsInt32().Nullable()
                .WithColumn("width").AsInt32().Nullable()
                .WithColumn("height").AsInt32().Nullable()
                .WithColumn("hdr_format").AsCustom("VARCHAR(20)").Nullable()
                .WithColumn("frame_rate").AsDouble().Nullable()
                .WithColumn("is_default").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("is_forced").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("is_hearing_impaired").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("is_commentary").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("is_descriptive").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("is_text_based").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("is_external").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("external_path").AsCustom("TEXT").Nullable()
                .WithColumn("event_count").AsInt32().Nullable()
                .WithColumn("index_hint").AsInt32().Nullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_media_streams_media_file_id", "media_streams", "media_file_id", "media_files");
        }

        if (!Schema.Table("media_streams").Index("ix_media_streams_media_file_id").Exists())
        {
            Create.Index("ix_media_streams_media_file_id").OnTable("media_streams")
                .OnColumn("media_file_id").Ascending();
        }

        if (!Schema.Table("media_chapters").Exists())
        {
            Create.Table("media_chapters")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("media_file_id").AsGuid().NotNullable()
                    .ForeignKey("fk_media_chapters_media_file_id", "media_files", "id").OnDelete(Rule.Cascade)
                .WithColumn("sort_order").AsInt32().NotNullable()
                .WithColumn("title").AsCustom("TEXT").Nullable()
                .WithColumn("start_ms").AsInt64().NotNullable()
                .WithColumn("end_ms").AsInt64().Nullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_media_chapters_media_file_id", "media_chapters", "media_file_id", "media_files");
        }

        if (!Schema.Table("media_chapters").Index("ix_media_chapters_media_file_id").Exists())
        {
            Create.Index("ix_media_chapters_media_file_id").OnTable("media_chapters")
                .OnColumn("media_file_id").Ascending();
        }
    }

    public override void Down()
    {
        Delete.Table("media_chapters");
        Delete.Table("media_streams");

        Delete.Index("ix_media_files_probe_status").OnTable("media_files");
        Delete.Column("duration_ms")
            .Column("probe_status")
            .Column("probe_error")
            .Column("probed_at")
            .Column("probe_version")
            .Column("probed_size_bytes")
            .Column("probed_modified_at")
            .FromTable("media_files");
    }
}
