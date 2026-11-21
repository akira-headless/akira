using FluentMigrator;

namespace Akira.Migrations.Migrations;

[Migration(18)]
public class M0018_AddMarkerScanRecaps : Migration
{
    public override void Up()
    {
        if (Schema.Table("marker_scans").Column("recap_start_ms").Exists())
        {
            return;
        }

        Alter.Table("marker_scans")
            .AddColumn("recap_start_ms").AsInt64().Nullable()
            .AddColumn("recap_end_ms").AsInt64().Nullable();
    }

    public override void Down()
    {
        Delete.Column("recap_start_ms").Column("recap_end_ms").FromTable("marker_scans");
    }
}
