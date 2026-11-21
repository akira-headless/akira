using FluentMigrator;

namespace Akira.Migrations.Migrations;

[Migration(9)]
public class M0009_AddSessionStreamDecision : Migration
{
    public override void Up()
    {
        if (!Schema.Table("playback_sessions").Column("stream_decision").Exists())
        {
            Alter.Table("playback_sessions")
                .AddColumn("stream_decision").AsCustom("VARCHAR(255)").Nullable();
        }
    }

    public override void Down()
    {
        Delete.Column("stream_decision").FromTable("playback_sessions");
    }
}
