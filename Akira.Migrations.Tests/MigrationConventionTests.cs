using System.Reflection;
using System.Text.RegularExpressions;
using FluentMigrator;
using Akira.Infrastructure.Configuration;
using Xunit;

namespace Akira.Migrations.Tests;

public partial class MigrationConventionTests
{
    [Fact]
    public void MigrationNames_MatchTheirVersion()
    {
        Assert.All(MigrationHarness.MigrationTypes, type =>
        {
            var match = MigrationName().Match(type.Name);
            Assert.True(match.Success, $"{type.Name} does not follow M{{NNNN}}_{{Name}}.");
            Assert.Equal(type.GetCustomAttribute<MigrationAttribute>()!.Version, long.Parse(match.Groups[1].Value));
        });
    }

    [Fact]
    public void MigrationVersions_AreSequentialFromOne()
    {
        var versions = MigrationHarness.MigrationTypes
            .Select(type => type.GetCustomAttribute<MigrationAttribute>()!.Version)
            .Order()
            .ToList();

        Assert.Equal(Enumerable.Range(1, versions.Count).Select(version => (long)version), versions);
    }

    [Fact]
    public void Migrations_LiveInTheMigrationsNamespace()
    {
        Assert.All(MigrationHarness.MigrationTypes,
            type => Assert.Equal("Akira.Migrations.Migrations", type.Namespace));
    }

    [Fact]
    public void EveryDatabaseProvider_HasAProcessor()
    {
        Assert.All(Enum.GetValues<DatabaseProvider>(),
            provider => Assert.False(string.IsNullOrEmpty(MigrationConfiguration.GetProcessorId(provider))));
    }

    [GeneratedRegex(@"^M(\d{4})_[A-Za-z0-9]+$")]
    private static partial Regex MigrationName();
}
