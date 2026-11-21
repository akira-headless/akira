namespace Akira.Migrations;

/// <summary>
/// Defines the tokens for provider specific blocks, for example IfDatabase(MigrationDatabase.MariaDb).
/// </summary>
public static class MigrationDatabase
{
    // IfDatabase compares these case-insensitively against the processor type and its aliases.
    // A token that matches nothing compiles, throws nothing and silently skips its block, so
    // MigrationDatabaseTests checks each one against the processor that runs it.
    public const string Sqlite = "sqlite";
    public const string MariaDb = "mariadb";
}
