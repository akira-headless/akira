using FluentMigrator.Runner.VersionTableInfo;

namespace Akira.Migrations;

public class VersionTableMetaData : IVersionTableMetaData
{
    public object? ApplicationContext { get; set; }
    public bool OwnsSchema => true;
    public string SchemaName => string.Empty;
    public string TableName => "version_info";
    public string ColumnName => "version";
    public string UniqueIndexName => "ux_version";
    public string AppliedOnColumnName => "applied_on";
    public string DescriptionColumnName => "description";
    public bool CreateWithPrimaryKey => false;
}
