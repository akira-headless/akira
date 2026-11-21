using Akira.Testing;
using Xunit;

namespace Akira.Migrations.Tests;

public abstract class MigrationDatabaseTests
{
    protected abstract string Token { get; }

    protected abstract Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken);

    // Mirrors how IfDatabase decides: a case-insensitive match on the processor type or an alias
    [Fact]
    public async Task Token_MatchesTheProcessorThatRuns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateDatabaseAsync(cancellationToken);
        using var harness = new MigrationHarness(database);

        var processor = harness.GetProcessor();
        var names = processor.DatabaseTypeAliases.Prepend(processor.DatabaseType);

        Assert.Contains(Token, names, StringComparer.OrdinalIgnoreCase);
    }
}
