using FluentMigrator;

namespace Akira.Migrations.Migrations;

[Migration(11)]
public class M0011_AddOutgoingWebhooks : Migration
{
    public override void Up()
    {
        if (Schema.Table("outgoing_webhooks").Exists())
        {
            return;
        }

        Create.Table("outgoing_webhooks")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsCustom("VARCHAR(100)").NotNullable()
            .WithColumn("url").AsCustom("TEXT").NotNullable()
            .WithColumn("event_types").AsCustom("VARCHAR(1000)").NotNullable()
            .WithColumn("enabled").AsBoolean().NotNullable()
            .WithColumn("protected_secret").AsCustom("TEXT").NotNullable()
            .WithColumn("last_sequence").AsInt64().NotNullable()
            .WithColumn("consecutive_failures").AsInt32().NotNullable()
            .WithColumn("next_attempt_at").AsCustom("DATETIME(6)").Nullable()
            .WithColumn("last_delivered_at").AsCustom("DATETIME(6)").Nullable()
            .WithColumn("last_status_code").AsInt32().Nullable()
            .WithColumn("last_error").AsCustom("TEXT").Nullable()
            .WithColumn("created_at").AsCustom("DATETIME(6)").NotNullable()
            .WithColumn("updated_at").AsCustom("DATETIME(6)").NotNullable();
    }

    public override void Down()
    {
        Delete.Table("outgoing_webhooks");
    }
}
