using FluentMigrator;

namespace Akira.Migrations.Migrations;

// Intentionally empty. FluentMigrator throws MissingMigrationsException when the assembly holds
// no migrations, so this marks the schema origin and the first real table starts at M0002.
[Migration(1)]
public class M0001_Baseline : Migration
{
    public override void Up()
    {
    }

    public override void Down()
    {
    }
}
