using Xunit;

namespace Akira.Migrations.Tests;

public class SchemaComparerTests
{
    [Theory]
    [InlineData("int(11)", "int")]
    [InlineData("INTEGER", "int")]
    [InlineData("bigint(20)", "bigint")]
    [InlineData("tinyint(1)", "tinyint(1)")]
    [InlineData("datetime(6)", "datetime(6)")]
    [InlineData("VARCHAR(255)", "varchar(255)")]
    [InlineData(" longtext ", "longtext")]
    public void NormalizeColumnType_IgnoresIntegerDisplayWidthOnly(string columnType, string expected)
    {
        Assert.Equal(expected, SchemaComparer.NormalizeColumnType(columnType));
    }
}
