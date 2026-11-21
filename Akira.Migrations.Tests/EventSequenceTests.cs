using Microsoft.EntityFrameworkCore;
using Akira.Core.Entities;
using Akira.Testing;
using Xunit;

namespace Akira.Migrations.Tests;

// Clients resume from an event sequence. A sequence handed out twice would make one of them skip
// the event that reused it, which on SQLite takes AUTOINCREMENT rather than a plain rowid.
public abstract class EventSequenceTests
{
    protected abstract Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task DeletingTheNewestEvents_NeverHandsTheirSequenceOutAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateDatabaseAsync(cancellationToken);
        using (var harness = new MigrationHarness(database))
        {
            harness.MigrateUp();
        }

        await using var context = database.CreateAkiraContext();
        var first = Enumerable.Range(0, 3).Select(_ => Event()).ToList();
        context.Events.AddRange(first);
        await context.SaveChangesAsync(cancellationToken);
        await context.Events.ExecuteDeleteAsync(cancellationToken);

        var next = Event();
        context.Events.Add(next);
        await context.SaveChangesAsync(cancellationToken);

        Assert.Equal(first.Select(e => e.Sequence).Order(), first.Select(e => e.Sequence));
        Assert.True(next.Sequence > first.Max(e => e.Sequence));
    }

    private static OutboxEvent Event() => new() { Type = "test", Version = 1, Payload = "{}", CreatedAt = DateTimeOffset.UtcNow };
}
