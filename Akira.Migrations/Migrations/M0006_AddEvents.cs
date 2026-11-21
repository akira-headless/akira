using System.Data;
using FluentMigrator;

namespace Akira.Migrations.Migrations;

[Migration(6)]
public class M0006_AddEvents : GuardedMigration
{
    public override void Up()
    {
        // Identity is AUTOINCREMENT on SQLite, so a sequence is never handed out twice even after
        // the newest events were deleted. Clients resume from it, a reused number would skip events.
        if (!Schema.Table("events").Exists())
        {
            Create.Table("events")
                .WithColumn("sequence").AsInt64().NotNullable().PrimaryKey().Identity()
                .WithColumn("type").AsCustom("VARCHAR(64)").NotNullable()
                .WithColumn("version").AsInt32().NotNullable()
                .WithColumn("payload").AsCustom("TEXT").NotNullable()
                .WithColumn("user_id").AsGuid().Nullable()
                    .ForeignKey("fk_events_user_id", "users", "id").OnDelete(Rule.Cascade)
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_events_user_id", "events", "user_id", "users");
        }

        if (!Schema.Table("events").Index("ix_events_created_at").Exists())
        {
            Create.Index("ix_events_created_at").OnTable("events").OnColumn("created_at").Ascending();
        }

        // Deleting a user cascades to their events
        if (!Schema.Table("events").Index("ix_events_user_id").Exists())
        {
            Create.Index("ix_events_user_id").OnTable("events").OnColumn("user_id").Ascending();
        }
    }

    public override void Down()
    {
        Delete.Table("events");
    }
}
