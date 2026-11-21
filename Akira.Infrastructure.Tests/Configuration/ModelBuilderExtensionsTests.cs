using Microsoft.EntityFrameworkCore;
using Akira.Infrastructure.Configuration;
using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Configuration;

public abstract class ModelBuilderExtensionsTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 3, 29, 1, 30, 0, 123, 456, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset Later = new(2026, 3, 29, 0, 45, 0, TimeSpan.Zero);

    protected abstract Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task UseUtcTimestamps_RoundTripsAsUtc()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SeedAsync(cancellationToken);

        await using var context = CreateContext(database);
        var row = await context.TimestampRows.SingleAsync(r => r.Name == "earlier", cancellationToken);

        Assert.Equal(Earlier, row.At);
        Assert.Equal(TimeSpan.Zero, row.At.Offset);
        Assert.Null(row.DeletedAt);
    }

    [Fact]
    public async Task UseUtcTimestamps_OrdersAndFiltersByInstant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SeedAsync(cancellationToken);

        await using var context = CreateContext(database);
        var ordered = await context.TimestampRows.OrderBy(r => r.At).Select(r => r.Name).ToListAsync(cancellationToken);
        var after = await context.TimestampRows.Where(r => r.At > Earlier).Select(r => r.Name).ToListAsync(cancellationToken);

        // Earlier is 23:30 UTC, so a comparison on the local wall clock would get both of these wrong
        Assert.Equal(["earlier", "later"], ordered);
        Assert.Equal(["later"], after);
    }

    [Fact]
    public async Task SnakeCaseNamingConvention_AppliesToTablesAndColumns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SeedAsync(cancellationToken);

        var columns = await database.GetColumnsAsync("timestamp_rows", cancellationToken);

        Assert.Equal(["id", "name", "at", "deleted_at"], columns.Select(column => column.Name));
    }

    private async Task<TestDatabase> SeedAsync(CancellationToken cancellationToken)
    {
        var database = await CreateDatabaseAsync(cancellationToken);
        await using var context = CreateContext(database);
        await context.Database.EnsureCreatedAsync(cancellationToken);
        context.TimestampRows.AddRange(
            new TimestampRow { Name = "later", At = Later },
            new TimestampRow { Name = "earlier", At = Earlier });
        await context.SaveChangesAsync(cancellationToken);
        return database;
    }

    private static TimestampContext CreateContext(TestDatabase database) =>
        database.CreateContext<TimestampContext>(options => new TimestampContext(options));

    private sealed class TimestampContext(DbContextOptions<TimestampContext> options) : DbContext(options)
    {
        public DbSet<TimestampRow> TimestampRows => Set<TimestampRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TimestampRow>().Property(row => row.Name).HasMaxLength(50);
            modelBuilder.UseUtcTimestamps();
        }
    }

    private sealed class TimestampRow
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public DateTimeOffset At { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }
    }
}
