using System.Data;
using FluentMigrator;

namespace Akira.Migrations.Migrations;

[Migration(4)]
public class M0004_AddAuth : GuardedMigration
{
    public override void Up()
    {
        if (!Schema.Table("users").Exists())
        {
            Create.Table("users")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("name").AsCustom("VARCHAR(100)").NotNullable()
                .WithColumn("normalized_name").AsCustom("VARCHAR(100)").NotNullable()
                .WithColumn("role").AsCustom("VARCHAR(20)").NotNullable()
                .WithColumn("pin_hash").AsCustom("VARCHAR(200)").Nullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }

        if (!Schema.Table("users").Index("ux_users_normalized_name").Exists())
        {
            Create.Index("ux_users_normalized_name").OnTable("users")
                .OnColumn("normalized_name").Ascending().WithOptions().Unique();
        }

        if (!Schema.Table("devices").Exists())
        {
            Create.Table("devices")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("name").AsCustom("VARCHAR(100)").NotNullable()
                .WithColumn("client").AsCustom("VARCHAR(50)").NotNullable()
                .WithColumn("client_version").AsCustom("VARCHAR(50)").Nullable()
                .WithColumn("platform").AsCustom("VARCHAR(50)").Nullable()
                .WithColumn("kind").AsCustom("VARCHAR(20)").NotNullable()
                .WithColumn("user_id").AsGuid().Nullable()
                    .ForeignKey("fk_devices_user_id", "users", "id").OnDelete(Rule.Cascade)
                .WithColumn("last_seen_at").AsCustom("DATETIME(6)").Nullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_devices_user_id", "devices", "user_id", "users");
        }

        if (!Schema.Table("devices").Index("ix_devices_user_id").Exists())
        {
            Create.Index("ix_devices_user_id").OnTable("devices").OnColumn("user_id").Ascending();
        }

        if (!Schema.Table("device_tokens").Exists())
        {
            Create.Table("device_tokens")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("device_id").AsGuid().NotNullable()
                    .ForeignKey("fk_device_tokens_device_id", "devices", "id").OnDelete(Rule.Cascade)
                .WithColumn("user_id").AsGuid().Nullable()
                    .ForeignKey("fk_device_tokens_user_id", "users", "id").OnDelete(Rule.Cascade)
                .WithColumn("token_hash").AsCustom("VARCHAR(64)").NotNullable()
                .WithColumn("expires_at").AsCustom("DATETIME(6)").Nullable()
                .WithColumn("last_used_at").AsCustom("DATETIME(6)").Nullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_device_tokens_device_id", "device_tokens", "device_id", "devices");
            AddForeignKeyIfMissing("fk_device_tokens_user_id", "device_tokens", "user_id", "users");
        }

        if (!Schema.Table("device_tokens").Index("ux_device_tokens_token_hash").Exists())
        {
            Create.Index("ux_device_tokens_token_hash").OnTable("device_tokens")
                .OnColumn("token_hash").Ascending().WithOptions().Unique();
        }

        if (!Schema.Table("device_tokens").Index("ix_device_tokens_device_id").Exists())
        {
            Create.Index("ix_device_tokens_device_id").OnTable("device_tokens").OnColumn("device_id").Ascending();
        }

        if (!Schema.Table("device_tokens").Index("ix_device_tokens_user_id").Exists())
        {
            Create.Index("ix_device_tokens_user_id").OnTable("device_tokens").OnColumn("user_id").Ascending();
        }

        if (!Schema.Table("pairing_requests").Exists())
        {
            Create.Table("pairing_requests")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("code").AsCustom("VARCHAR(16)").NotNullable()
                .WithColumn("poll_secret_hash").AsCustom("VARCHAR(64)").NotNullable()
                .WithColumn("device_name").AsCustom("VARCHAR(100)").NotNullable()
                .WithColumn("client").AsCustom("VARCHAR(50)").NotNullable()
                .WithColumn("client_version").AsCustom("VARCHAR(50)").Nullable()
                .WithColumn("platform").AsCustom("VARCHAR(50)").Nullable()
                .WithColumn("status").AsCustom("VARCHAR(20)").NotNullable()
                .WithColumn("expires_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("device_id").AsGuid().Nullable()
                    .ForeignKey("fk_pairing_requests_device_id", "devices", "id").OnDelete(Rule.Cascade)
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_pairing_requests_device_id", "pairing_requests", "device_id", "devices");
        }

        if (!Schema.Table("pairing_requests").Index("ix_pairing_requests_code").Exists())
        {
            Create.Index("ix_pairing_requests_code").OnTable("pairing_requests").OnColumn("code").Ascending();
        }

        if (!Schema.Table("pairing_requests").Index("ix_pairing_requests_device_id").Exists())
        {
            Create.Index("ix_pairing_requests_device_id").OnTable("pairing_requests").OnColumn("device_id").Ascending();
        }

        if (!Schema.Table("api_keys").Exists())
        {
            Create.Table("api_keys")
                .WithColumn("id").AsGuid().PrimaryKey()
                .WithColumn("user_id").AsGuid().NotNullable()
                    .ForeignKey("fk_api_keys_user_id", "users", "id").OnDelete(Rule.Cascade)
                .WithColumn("name").AsCustom("VARCHAR(100)").NotNullable()
                .WithColumn("key_hash").AsCustom("VARCHAR(64)").NotNullable()
                .WithColumn("prefix").AsCustom("VARCHAR(20)").NotNullable()
                .WithColumn("last_used_at").AsCustom("DATETIME(6)").Nullable()
                .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
                .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
        }
        else
        {
            AddForeignKeyIfMissing("fk_api_keys_user_id", "api_keys", "user_id", "users");
        }

        if (!Schema.Table("api_keys").Index("ux_api_keys_key_hash").Exists())
        {
            Create.Index("ux_api_keys_key_hash").OnTable("api_keys")
                .OnColumn("key_hash").Ascending().WithOptions().Unique();
        }

        if (!Schema.Table("api_keys").Index("ix_api_keys_user_id").Exists())
        {
            Create.Index("ix_api_keys_user_id").OnTable("api_keys").OnColumn("user_id").Ascending();
        }
    }

    public override void Down()
    {
        Delete.Table("api_keys");
        Delete.Table("pairing_requests");
        Delete.Table("device_tokens");
        Delete.Table("devices");
        Delete.Table("users");
    }
}
