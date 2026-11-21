using System.Data;
using FluentMigrator;

namespace Akira.Migrations.Migrations;

[Migration(7)]
public class M0007_AddActions : GuardedMigration
{
    public override void Up()
    {
        // No foreign keys on purpose: the audit record outlives the user, device and item it names
        if (!Schema.Table("audit_entries").Exists())
        {
            Create.Table("audit_entries")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("user_id").AsGuid().Nullable()
                .WithColumn("user_name").AsCustom("VARCHAR(100)").NotNullable()
                .WithColumn("device_id").AsGuid().Nullable()
                .WithColumn("action").AsCustom("VARCHAR(30)").NotNullable()
                .WithColumn("status").AsCustom("VARCHAR(30)").NotNullable()
                .WithColumn("scope").AsCustom("VARCHAR(20)").Nullable()
                .WithColumn("item_id").AsGuid().Nullable()
                .WithColumn("identity_key").AsCustom("VARCHAR(100)").NotNullable()
                .WithColumn("title").AsCustom("TEXT").NotNullable()
                .WithColumn("details").AsCustom("TEXT").NotNullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }

        if (!Schema.Table("audit_entries").Index("ix_audit_entries_created_at").Exists())
        {
            Create.Index("ix_audit_entries_created_at").OnTable("audit_entries").OnColumn("created_at").Ascending();
        }

        if (!Schema.Table("removal_settings").Exists())
        {
            Create.Table("removal_settings")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("scope_key").AsCustom("VARCHAR(50)").NotNullable()
                .WithColumn("user_id").AsGuid().Nullable()
                    .ForeignKey("fk_removal_settings_user_id", "users", "id").OnDelete(Rule.Cascade)
                .WithColumn("movie").AsCustom("VARCHAR(30)").Nullable()
                .WithColumn("episode").AsCustom("VARCHAR(30)").Nullable()
                .WithColumn("season").AsCustom("VARCHAR(30)").Nullable()
                .WithColumn("series").AsCustom("VARCHAR(30)").Nullable()
                .WithColumn("users_may_remove").AsBoolean().Nullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_removal_settings_user_id", "removal_settings", "user_id", "users");
        }

        if (!Schema.Table("removal_settings").Index("ux_removal_settings_scope_key").Exists())
        {
            Create.Index("ux_removal_settings_scope_key").OnTable("removal_settings").OnColumn("scope_key").Ascending().WithOptions().Unique();
        }

        if (!Schema.Table("removal_settings").Index("ix_removal_settings_user_id").Exists())
        {
            Create.Index("ix_removal_settings_user_id").OnTable("removal_settings").OnColumn("user_id").Ascending();
        }
    }

    public override void Down()
    {
        Delete.Table("removal_settings");
        Delete.Table("audit_entries");
    }
}
