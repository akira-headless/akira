using System.Data;
using FluentMigrator;

namespace Akira.Migrations.Migrations;

[Migration(12)]
public class M0012_AddTrakt : GuardedMigration
{
    public override void Up()
    {
        if (!Schema.Table("trakt_links").Exists())
        {
            Create.Table("trakt_links")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("user_id").AsGuid().NotNullable()
                    .ForeignKey("fk_trakt_links_user_id", "users", "id").OnDelete(Rule.Cascade)
                .WithColumn("username").AsCustom("VARCHAR(100)").NotNullable()
                .WithColumn("protected_access_token").AsCustom("TEXT").NotNullable()
                .WithColumn("protected_refresh_token").AsCustom("TEXT").NotNullable()
                .WithColumn("expires_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("last_import_at").AsCustom("DATETIME(6)").Nullable()
                .WithColumn("last_synced_at").AsCustom("DATETIME(6)").Nullable()
                .WithColumn("last_error").AsCustom("TEXT").Nullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_trakt_links_user_id", "trakt_links", "user_id", "users");
        }

        if (!Schema.Table("trakt_links").Index("ix_trakt_links_user_id").Exists())
        {
            Create.Index("ix_trakt_links_user_id").OnTable("trakt_links").OnColumn("user_id").Ascending().WithOptions().Unique();
        }

        if (!Schema.Table("event_cursors").Exists())
        {
            Create.Table("event_cursors")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("consumer").AsCustom("VARCHAR(50)").NotNullable()
                .WithColumn("last_sequence").AsInt64().NotNullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }

        if (!Schema.Table("event_cursors").Index("ix_event_cursors_consumer").Exists())
        {
            Create.Index("ix_event_cursors_consumer").OnTable("event_cursors").OnColumn("consumer").Ascending().WithOptions().Unique();
        }
    }

    public override void Down()
    {
        Delete.Table("event_cursors");
        Delete.Table("trakt_links");
    }
}
