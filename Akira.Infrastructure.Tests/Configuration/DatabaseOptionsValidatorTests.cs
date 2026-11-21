using Akira.Infrastructure.Configuration;
using Xunit;

namespace Akira.Infrastructure.Tests.Configuration;

public class DatabaseOptionsValidatorTests
{
    private readonly DatabaseOptionsValidator _validator = new();

    [Fact]
    public void Sqlite_WithAPath_AndMariaDbWithoutOne_AreValid()
    {
        Assert.True(_validator.Validate(null, new DatabaseOptions { SqlitePath = "akira.db" }).Succeeded);
        Assert.True(_validator.Validate(null, new DatabaseOptions
        {
            Provider = DatabaseProvider.MariaDb,
            Host = "nas",
            Name = "akira",
            Username = "akira",
            Password = "secret"
        }).Succeeded);
    }

    [Fact]
    public void Sqlite_WithoutPath_Fails()
    {
        var result = _validator.Validate(null, new DatabaseOptions());

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("Database:SqlitePath"));
    }

    [Fact]
    public void MariaDb_WithoutSettings_ListsEveryMissingOne()
    {
        var result = _validator.Validate(null, new DatabaseOptions { Provider = DatabaseProvider.MariaDb });

        Assert.True(result.Failed);
        Assert.Equal(
            ["Database:Host", "Database:Name", "Database:Username", "Database:Password"],
            result.Failures!.Select(failure => failure.Split(' ')[0]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void MariaDb_WithInvalidPort_Fails(int port)
    {
        var result = _validator.Validate(null, new DatabaseOptions
        {
            Provider = DatabaseProvider.MariaDb,
            Host = "nas",
            Port = port,
            Name = "akira",
            Username = "akira",
            Password = "secret"
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("Database:Port"));
    }

    [Fact]
    public void UnknownProvider_Fails()
    {
        var result = _validator.Validate(null, new DatabaseOptions { Provider = (DatabaseProvider)99 });

        Assert.True(result.Failed);
    }
}
