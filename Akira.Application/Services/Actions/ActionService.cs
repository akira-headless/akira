using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Server.Authentication;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Server.Events;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Interfaces.Integrations;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Actions;
using Akira.Server.Models.Events;
using Akira.Server.Models.Items;
using Akira.Server.Webhooks;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Akira.Server.Services.Actions;

public class ActionService : IActionService
{
    private readonly AkiraDbContext _dbContext;
    private readonly IArrClientFactory _clients;
    private readonly IOptions<ArrOptions> _arrOptions;
    private readonly IRemovalSettingsService _removal;
    private readonly LibraryRefreshQueue _refreshes;
    private readonly IEventPublisher _events;
    private readonly IOptions<JsonOptions> _jsonOptions;
    private readonly TimeProvider _time;
    private readonly ILogger<ActionService> _logger;

    public ActionService(
        AkiraDbContext dbContext,
        IArrClientFactory clients,
        IOptions<ArrOptions> arrOptions,
        IRemovalSettingsService removal,
        LibraryRefreshQueue refreshes,
        IEventPublisher events,
        IOptions<JsonOptions> jsonOptions,
        TimeProvider time,
        ILogger<ActionService> logger)
    {
        _dbContext = dbContext;
        _clients = clients;
        _arrOptions = arrOptions;
        _removal = removal;
        _refreshes = refreshes;
        _events = events;
        _jsonOptions = jsonOptions;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<ServiceResult<ActionResult>> DeleteAsync(
        ClaimsPrincipal user,
        Guid itemId,
        DeleteActionRequest request,
        CancellationToken cancellationToken)
    {
        // Every part is required, so a client never removes more than it said it would
        if (request.Scope is null || request.DeleteFiles is null || request.Unmonitor is null || request.Instances is not { Count: > 0 })
        {
            return Invalid("scope, deleteFiles, unmonitor and instances are all required.");
        }

        if (request is { DeleteFiles: false, Unmonitor: false })
        {
            return Invalid("deleteFiles and unmonitor are both false, so there is nothing to do.");
        }

        if (request.Search && (request.Unmonitor == true || request.DeleteFiles == false))
        {
            return Invalid("search fetches a new copy, so it needs deleteFiles true and unmonitor false.");
        }

        var kind = request.DeleteFiles == true ? ActionKind.Delete : ActionKind.Unmonitor;
        var plan = new Requested(kind, request.Scope.Value, request.Instances, request.DeleteFiles.Value, request.Unmonitor.Value, request.Search,
            request.DryRun, request);
        return RunAsync(user, itemId, plan, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ServiceResult<ActionResult>> UnmonitorAsync(
        ClaimsPrincipal user,
        Guid itemId,
        UnmonitorActionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Scope is null || request.Instances is not { Count: > 0 })
        {
            return Invalid("scope and instances are both required.");
        }

        var plan = new Requested(ActionKind.Unmonitor, request.Scope.Value, request.Instances, false, true, false, request.DryRun, request);
        return RunAsync(user, itemId, plan, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<MarkPlayedResult>> MarkPlayedAsync(
        ClaimsPrincipal user,
        Guid itemId,
        MarkPlayedRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Played is not { } played)
        {
            return ServiceResult<MarkPlayedResult>.Fail(ServiceError.Invalid, "played is required.");
        }

        var item = await _dbContext.MediaItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null)
        {
            return ServiceResult<MarkPlayedResult>.Fail(ServiceError.NotFound, $"Item {itemId} does not exist.");
        }

        var userId = user.GetUserId()!.Value;
        var targets = await FindWatchTargetsAsync(item, userId, played, cancellationToken);
        var keys = targets.Select(target => target.IdentityKey).ToList();
        var states = await _dbContext.WatchStates
            .Where(w => w.UserId == userId && keys.Contains(w.IdentityKey))
            .ToDictionaryAsync(w => w.IdentityKey, cancellationToken);

        var now = _time.GetUtcNow();
        foreach (var target in targets)
        {
            if (!states.TryGetValue(target.IdentityKey, out var state))
            {
                state = new WatchState { UserId = userId, IdentityKey = target.IdentityKey };
                _dbContext.WatchStates.Add(state);
                states[target.IdentityKey] = state;
            }

            if (played && !state.IsPlayed)
            {
                state.PlayCount++;
                state.LastPlayedAt = now;
            }

            state.IsPlayed = played;
            state.PositionMs = 0;
        }

        var kind = played ? ActionKind.MarkPlayed : ActionKind.MarkUnplayed;
        var title = await DisplayTitleAsync(item, cancellationToken);
        var audit = Audit(user, kind, ActionStatus.Succeeded, ScopeOf(item.Type), item, title, new { request, items = targets.Count });
        await _dbContext.SaveChangesAsync(cancellationToken);

        var drafts = targets.Select(target =>
        {
            var state = states[target.IdentityKey];
            var payload = new WatchStatePayload(target.Id, target.IdentityKey, state.PositionMs, state.IsPlayed, state.PlayCount,
                state.LastPlayedAt, WatchStateReasons.Marked);
            return new EventDraft(EventTypes.WatchStateChanged, payload, userId);
        }).ToList();
        await _events.PublishAsync(drafts);

        return ServiceResult<MarkPlayedResult>.Ok(new MarkPlayedResult(audit.Id, played, targets.Count));
    }

    /// <inheritdoc />
    public async Task<PagedResponse<AuditEntryModel>> ListAuditAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        var total = await _dbContext.AuditEntries.CountAsync(cancellationToken);
        var rows = await _dbContext.AuditEntries.AsNoTracking()
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var items = rows.Select(row =>
        {
            using var details = JsonDocument.Parse(row.Details);
            return new AuditEntryModel(row.Id, row.CreatedAt, row.UserId, row.UserName, row.DeviceId, row.Action, row.Status, row.Scope,
                row.ItemId, row.IdentityKey, row.Title, details.RootElement.Clone());
        }).ToList();
        return new PagedResponse<AuditEntryModel>(items, total, offset, limit);
    }

    private async Task<ServiceResult<ActionResult>> RunAsync(ClaimsPrincipal user, Guid itemId, Requested requested, CancellationToken cancellationToken)
    {
        if (!await _removal.MayRemoveAsync(user, cancellationToken))
        {
            return ServiceResult<ActionResult>.Fail(ServiceError.Forbidden,
                "On this server only admins remove. An admin can allow it with usersMayRemove in PUT /api/v1/settings/removal.");
        }

        var item = await _dbContext.MediaItems.AsNoTracking()
            .Include(i => i.Links)
            .SingleOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null)
        {
            return ServiceResult<ActionResult>.Fail(ServiceError.NotFound, $"Item {itemId} does not exist.");
        }

        // The scope repeats what the client thinks it is removing. Saying season about a series is refused.
        if (ScopeOf(item.Type) != requested.Scope)
        {
            return ServiceResult<ActionResult>.Fail(ServiceError.Invalid,
                $"{item.Title} is a {Name(item.Type)}, the request says {Name(requested.Scope)}.");
        }

        var instances = new List<ArrInstanceOptions>();
        foreach (var key in requested.Instances.Distinct())
        {
            var instance = _arrOptions.Value.Instances.FirstOrDefault(instance => instance.Key == key);
            if (instance is null || item.Links.All(link => link.InstanceKey != key))
            {
                return ServiceResult<ActionResult>.Fail(ServiceError.Invalid, $"{item.Title} is not in an instance called {key}.");
            }

            instances.Add(instance);
        }

        var plans = new List<InstancePlan>();
        foreach (var instance in instances)
        {
            plans.Add(await PlanAsync(item, instance, cancellationToken));
        }

        if (requested.DryRun)
        {
            var planned = plans.Select(plan => Outcome(plan, ActionStatus.Planned, new Progress(), plan.Problem)).ToList();
            return ServiceResult<ActionResult>.Ok(new ActionResult(null, requested.Kind, ActionStatus.Planned, item.Id, item.Title,
                requested.Scope, planned));
        }

        // In the log before Radarr or Sonarr hear of it. Should the server stop halfway, the entry still says what
        // was started and how far it came.
        var outcomes = new List<InstanceOutcome>();
        var title = await DisplayTitleAsync(item, cancellationToken);
        var audit = Audit(user, requested.Kind, ActionStatus.Started, requested.Scope, item, title, new { request = requested.Body, instances = outcomes });
        await _dbContext.SaveChangesAsync(cancellationToken);

        // From here on a client that hangs up stops nothing: files may already be gone, and the audit entry must say so
        var uncancelled = CancellationToken.None;
        foreach (var plan in plans)
        {
            outcomes.Add(await ExecuteAsync(plan, requested, uncancelled));
            audit.Details = Serialize(new { request = requested.Body, instances = outcomes });
            await _dbContext.SaveChangesAsync(uncancelled);
        }

        var status = OverallStatus(outcomes);
        audit.Status = status;
        await _dbContext.SaveChangesAsync(uncancelled);
        _logger.LogInformation("{User} {Action} {Title} ({Scope}) in {Instances}: {Status}, {Deleted} files deleted",
            audit.UserName, requested.Kind, title, requested.Scope, string.Join(", ", instances.Select(i => i.Key)), status,
            outcomes.Sum(o => o.FilesDeleted));

        // The library follows in the background, at once and not after a webhook's wait. A full sync may be running,
        // and the answer does not wait for it.
        foreach (var plan in plans.Where(plan => plan.SubjectId is not null))
        {
            _refreshes.EnqueueNow(new LibraryRefresh(plan.Instance.Key, plan.SubjectId!.Value));
        }

        var instanceKeys = instances.Select(i => i.Key).ToList();
        var payload = new ActionCompletedPayload(audit.Id, requested.Kind, status, item.Id, requested.Scope, instanceKeys);
        await _events.PublishAsync([new EventDraft(EventTypes.ActionCompleted, payload)]);

        return ServiceResult<ActionResult>.Ok(new ActionResult(audit.Id, requested.Kind, status, item.Id, item.Title, requested.Scope, outcomes));
    }

    private async Task<InstancePlan> PlanAsync(MediaItem item, ArrInstanceOptions instance, CancellationToken cancellationToken)
    {
        if (item.Type == MediaItemType.Movie)
        {
            var movieId = item.Links.Single(link => link.InstanceKey == instance.Key).ArrId;
            var movieFiles = await FilesAsync([item.Id], instance.Key, cancellationToken);
            var movieProblem = movieId is null ? $"{instance.DisplayName} has no id for it." : null;
            return new InstancePlan(instance, movieId, null, [], movieFiles, movieProblem);
        }

        // Seasons and episodes act through their series, which is what Sonarr monitors and syncs
        var series = item.Type switch
        {
            MediaItemType.Series => item,
            MediaItemType.Season => await _dbContext.MediaItems.AsNoTracking().SingleAsync(i => i.Id == item.ParentId, cancellationToken),
            _ => await _dbContext.MediaItems.AsNoTracking()
                .Where(i => i.Id == item.ParentId)
                .Select(i => i.Parent!)
                .SingleAsync(cancellationToken)
        };
        var seriesId = await _dbContext.ArrLinks
            .Where(link => link.MediaItemId == series.Id && link.InstanceKey == instance.Key)
            .Select(link => link.ArrId)
            .SingleOrDefaultAsync(cancellationToken);

        var prefix = series.IdentityKey + ":";
        var episodes = item.Type switch
        {
            MediaItemType.Episode => new List<Guid> { item.Id },
            MediaItemType.Season => await _dbContext.MediaItems
                .Where(i => i.ParentId == item.Id && i.Type == MediaItemType.Episode)
                .Select(i => i.Id)
                .ToListAsync(cancellationToken),
            _ => await _dbContext.MediaItems
                .Where(i => i.IdentityKey.StartsWith(prefix) && i.Type == MediaItemType.Episode)
                .Select(i => i.Id)
                .ToListAsync(cancellationToken)
        };

        var episodeIds = await _dbContext.ArrLinks
            .Where(link => episodes.Contains(link.MediaItemId) && link.InstanceKey == instance.Key && link.ArrId != null)
            .Select(link => link.ArrId!.Value)
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);
        var files = await FilesAsync(episodes, instance.Key, cancellationToken);
        var season = item.Type == MediaItemType.Season ? item.SeasonNumber : null;
        var problem = seriesId is null ? $"{instance.DisplayName} has no id for the series." : null;
        return new InstancePlan(instance, seriesId, season, episodeIds, files, problem);
    }

    private async Task<InstanceOutcome> ExecuteAsync(InstancePlan plan, Requested requested, CancellationToken cancellationToken)
    {
        var progress = new Progress();
        if (plan.Problem is not null)
        {
            return Outcome(plan, ActionStatus.Failed, progress, plan.Problem);
        }

        try
        {
            // Unmonitored before the files go, so an RSS sync in between cannot grab it again
            if (plan.Instance.Kind == ArrKind.Radarr)
            {
                var radarr = _clients.CreateRadarr(plan.Instance);
                if (requested.Unmonitor)
                {
                    await radarr.SetMonitoredAsync([plan.SubjectId!.Value], false, cancellationToken);
                    progress.Unmonitored = 1;
                }

                if (requested.DeleteFiles)
                {
                    foreach (var file in plan.Files)
                    {
                        await DeleteAsync(progress, () => radarr.DeleteMovieFileAsync(file.ArrFileId, cancellationToken));
                    }
                }

                if (requested.Search)
                {
                    await radarr.SearchAsync([plan.SubjectId!.Value], cancellationToken);
                    progress.Searched = true;
                }
            }
            else
            {
                var sonarr = _clients.CreateSonarr(plan.Instance);
                if (requested.Unmonitor)
                {
                    // A season or series is unmonitored as such too, so the episodes Sonarr adds later stay away
                    if (requested.Scope is ActionScope.Season or ActionScope.Series)
                    {
                        await sonarr.SetSeriesMonitoredAsync(plan.SubjectId!.Value, plan.SeasonNumber, false, cancellationToken);
                    }

                    if (plan.EpisodeIds.Count > 0)
                    {
                        await sonarr.SetEpisodesMonitoredAsync(plan.EpisodeIds, false, cancellationToken);
                    }

                    progress.Unmonitored = plan.EpisodeIds.Count;
                }

                if (requested.DeleteFiles)
                {
                    foreach (var file in plan.Files)
                    {
                        await DeleteAsync(progress, () => sonarr.DeleteEpisodeFileAsync(file.ArrFileId, cancellationToken));
                    }
                }

                if (requested.Search && plan.EpisodeIds.Count > 0)
                {
                    await sonarr.SearchEpisodesAsync(plan.EpisodeIds, cancellationToken);
                    progress.Searched = true;
                }
            }
        }
        catch (Exception exception)
        {
            // Whatever it is, it is this instance's outcome: the ones after it still run, and the log says how far this one came
            if (exception is not UpstreamException)
            {
                _logger.LogError(exception, "{Action} in {Instance} stopped on an error nobody expected", requested.Kind, plan.Instance.DisplayName);
            }

            var status = progress.Deleted + progress.Unmonitored > 0 ? ActionStatus.PartiallySucceeded : ActionStatus.Failed;
            return Outcome(plan, status, progress, exception.Message);
        }

        return Outcome(plan, ActionStatus.Succeeded, progress, null);
    }

    private static async Task DeleteAsync(Progress progress, Func<Task> delete)
    {
        try
        {
            await delete();
            progress.Deleted++;
        }
        catch (UpstreamException exception) when (exception.Failure == UpstreamFailure.NotFound)
        {
            // Already deleted, by someone else or an earlier attempt that failed halfway. What was asked for is true,
            // so it is no failure, but this action did not delete it and the log does not say it did.
            progress.AlreadyGone++;
        }
    }

    private async Task<List<WatchTarget>> FindWatchTargetsAsync(MediaItem item, Guid userId, bool played, CancellationToken cancellationToken)
    {
        if (item.Type is MediaItemType.Movie or MediaItemType.Episode)
        {
            return [new WatchTarget(item.Id, item.IdentityKey)];
        }

        // Marked played: what can be watched. Marked unplayed: everything that has a state.
        var episodes = _dbContext.MediaItems.AsNoTracking().Where(i => i.Type == MediaItemType.Episode);
        if (item.Type == MediaItemType.Season)
        {
            episodes = episodes.Where(i => i.ParentId == item.Id);
        }
        else
        {
            var prefix = item.IdentityKey + ":";
            episodes = episodes.Where(i => i.IdentityKey.StartsWith(prefix));
        }

        episodes = played
            ? episodes.Where(i => i.Files.Any())
            : episodes.Where(i => _dbContext.WatchStates.Any(w => w.UserId == userId && w.IdentityKey == i.IdentityKey));

        return await episodes
            .OrderBy(i => i.SeasonNumber)
            .ThenBy(i => i.EpisodeNumber)
            .Select(i => new WatchTarget(i.Id, i.IdentityKey))
            .ToListAsync(cancellationToken);
    }

    private Task<List<PlannedFile>> FilesAsync(List<Guid> itemIds, string instanceKey, CancellationToken cancellationToken) =>
        _dbContext.MediaFiles.AsNoTracking()
            .Where(file => itemIds.Contains(file.MediaItemId) && file.InstanceKey == instanceKey)
            .OrderBy(file => file.ArrFileId)
            .Select(file => new PlannedFile(file.ArrFileId, file.SizeBytes))
            .ToListAsync(cancellationToken);

    private async Task<string> DisplayTitleAsync(MediaItem item, CancellationToken cancellationToken)
    {
        // "Season 2" alone says nothing in an audit log read months later
        if (item.Type is not (MediaItemType.Season or MediaItemType.Episode))
        {
            return item.Title;
        }

        var parent = _dbContext.MediaItems.Where(i => i.Id == item.ParentId);
        if (item.Type == MediaItemType.Season)
        {
            var seasonSeriesTitle = await parent.Select(i => i.Title).SingleOrDefaultAsync(cancellationToken);
            return $"{seasonSeriesTitle} {item.Title}";
        }

        var seriesTitle = await parent.Select(i => i.Parent!.Title).SingleOrDefaultAsync(cancellationToken);
        return $"{seriesTitle} S{item.SeasonNumber:00}E{item.EpisodeNumber:00} {item.Title}";
    }

    private AuditEntry Audit(
        ClaimsPrincipal user,
        ActionKind action,
        ActionStatus status,
        ActionScope scope,
        MediaItem item,
        string title,
        object details)
    {
        var entry = new AuditEntry
        {
            UserId = user.GetUserId(),
            UserName = user.FindFirstValue(AkiraClaims.UserName) ?? "unknown",
            DeviceId = user.GetDeviceId(),
            Action = action,
            Status = status,
            Scope = scope,
            ItemId = item.Id,
            IdentityKey = item.IdentityKey,
            Title = title,
            Details = Serialize(details)
        };
        _dbContext.AuditEntries.Add(entry);
        return entry;
    }

    private string Serialize(object details) => JsonSerializer.Serialize(details, _jsonOptions.Value.SerializerOptions);

    private static InstanceOutcome Outcome(InstancePlan plan, ActionStatus status, Progress progress, string? error)
    {
        var bytes = plan.Files.Sum(file => file.Bytes);
        return new InstanceOutcome(plan.Instance.Key, plan.Instance.DisplayName, status, plan.Files.Count, bytes, progress.Deleted,
            progress.AlreadyGone, progress.Unmonitored, progress.Searched, error);
    }

    private static ActionStatus OverallStatus(List<InstanceOutcome> outcomes)
    {
        if (outcomes.All(o => o.Status == ActionStatus.Succeeded))
        {
            return ActionStatus.Succeeded;
        }

        if (outcomes.All(o => o.Status == ActionStatus.Failed))
        {
            return ActionStatus.Failed;
        }

        return ActionStatus.PartiallySucceeded;
    }

    private static ActionScope ScopeOf(MediaItemType type) => type switch
    {
        MediaItemType.Movie => ActionScope.Movie,
        MediaItemType.Season => ActionScope.Season,
        MediaItemType.Series => ActionScope.Series,
        _ => ActionScope.Episode
    };

    private static string Name<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static Task<ServiceResult<ActionResult>> Invalid(string detail) =>
        Task.FromResult(ServiceResult<ActionResult>.Fail(ServiceError.Invalid, detail));

    /// <summary>
    /// Represents a destructive action as requested. Body is the original request, kept for the audit entry.
    /// </summary>
    private sealed record Requested(
        ActionKind Kind,
        ActionScope Scope,
        IReadOnlyList<string> Instances,
        bool DeleteFiles,
        bool Unmonitor,
        bool Search,
        bool DryRun,
        object Body);

    /// <summary>
    /// Represents what an action does on one instance. SubjectId is the Radarr movie id or the Sonarr series id.
    /// </summary>
    private sealed record InstancePlan(
        ArrInstanceOptions Instance,
        int? SubjectId,
        int? SeasonNumber,
        List<int> EpisodeIds,
        List<PlannedFile> Files,
        string? Problem);

    /// <summary>
    /// Counts what an action did in one instance so far, so a failure halfway reports what came before it.
    /// </summary>
    private sealed class Progress
    {
        public int Deleted { get; set; }
        public int AlreadyGone { get; set; }
        public int Unmonitored { get; set; }
        public bool Searched { get; set; }
    }

    /// <summary>
    /// Represents a Radarr or Sonarr file an action deletes, with its size in bytes.
    /// </summary>
    private sealed record PlannedFile(int ArrFileId, long Bytes);

    /// <summary>
    /// Represents a movie or episode whose watch state is marked played or unplayed.
    /// </summary>
    private sealed record WatchTarget(Guid Id, string IdentityKey);
}
