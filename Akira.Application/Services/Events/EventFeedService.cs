using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Server.Events;
using Akira.Infrastructure.Data;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Events;

namespace Akira.Server.Services.Events;

public class EventFeedService : IEventFeedService
{
    private readonly AkiraDbContext _dbContext;
    private readonly IOptions<EventOptions> _options;
    private readonly TimeProvider _time;

    public EventFeedService(
        AkiraDbContext dbContext,
        IOptions<EventOptions> options,
        TimeProvider time)
    {
        _dbContext = dbContext;
        _options = options;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<EventPage> GetSinceAsync(Guid? userId, long since, int limit, CancellationToken cancellationToken)
    {
        // Read first and used as the upper bound, so an event saved while this runs is either in
        // the page or comes after it, never neither
        var bounds = await _dbContext.Events.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new { Oldest = group.Min(e => e.Sequence), Latest = group.Max(e => e.Sequence) })
            .SingleOrDefaultAsync(cancellationToken);
        long oldest = 0;
        long latest = 0;
        if (bounds is not null)
        {
            oldest = bounds.Oldest;
            latest = bounds.Latest;
        }

        // Gone means deleted after the retention, or a cursor from another database: the client cannot catch up event by event
        if (since > latest || (oldest > 0 && since < oldest - 1))
        {
            return new EventPage([], latest, false, true);
        }

        var rows = await _dbContext.Events.AsNoTracking()
            .Where(e => e.Sequence > since && e.Sequence <= latest && (e.UserId == null || e.UserId == userId))
            .OrderBy(e => e.Sequence)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > limit;
        var messages = rows.Take(limit).Select(EventPublisher.ToMessage).ToList();
        return new EventPage(messages, latest, hasMore, false);
    }

    /// <inheritdoc />
    public async Task<long> GetLatestAsync(CancellationToken cancellationToken)
    {
        var latest = await _dbContext.Events.MaxAsync(e => (long?)e.Sequence, cancellationToken);
        return latest ?? 0;
    }

    /// <inheritdoc />
    public async Task<int> PruneAsync(CancellationToken cancellationToken)
    {
        var latest = await GetLatestAsync(cancellationToken);
        var cutoff = _time.GetUtcNow() - _options.Value.Retention;
        return await _dbContext.Events
            .Where(e => e.CreatedAt < cutoff && e.Sequence < latest)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
