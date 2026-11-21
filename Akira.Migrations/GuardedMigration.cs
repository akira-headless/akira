using System.Data;
using FluentMigrator;

namespace Akira.Migrations;

// MariaDB commits every DDL statement, so a migration that fails halfway keeps what it did and gets no version row.
// Its steps check what exists first, so the next start finishes the work instead of failing on it.
public abstract class GuardedMigration : Migration
{
    // FluentMigrator adds a table's foreign keys on MariaDB in statements of their own after the table, so a table
    // may exist without them. SQLite writes them into the table, which only exists whole.
    protected void AddForeignKeyIfMissing(string name, string table, string column, string primaryTable)
    {
        if (Schema.Table(table).Constraint(name).Exists())
        {
            return;
        }

        IfDatabase(MigrationDatabase.MariaDb).Create.ForeignKey(name)
            .FromTable(table).ForeignColumn(column)
            .ToTable(primaryTable).PrimaryColumn("id")
            .OnDelete(Rule.Cascade);
    }
}
