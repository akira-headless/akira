using FluentMigrator;

namespace Akira.Migrations.Migrations;

[Migration(20)]
public class M0020_AddPlaybackSessionStart : Migration
{
    public override void Up()
    {
        if (!Schema.Table("playback_sessions").Column("started_at").Exists())
        {
            Alter.Table("playback_sessions").AddColumn("started_at").AsCustom("DATETIME(6)").Nullable();
        }

        // Every session before this one started with its plan. Only rows without one, so a fill that failed after
        // the column was committed is done on the next run.
        const string fill = "UPDATE playback_sessions SET started_at = created_at WHERE started_at IS NULL";
        IfDatabase(MigrationDatabase.Sqlite).Execute.Sql(fill);
        IfDatabase(MigrationDatabase.MariaDb).Execute.Sql(fill);
    }

    public override void Down()
    {
        Delete.Column("started_at").FromTable("playback_sessions");
    }
}
