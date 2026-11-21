using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Media;
using Akira.Core.Playback;
using Akira.Server.Events;
using Akira.Infrastructure.Data;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Events;
using Akira.Server.Models.Playback;
using Akira.Server.Playback;

namespace Akira.Server.Services.Playback;

public class TrackPreferenceService : ITrackPreferenceService
{
    private readonly AkiraDbContext _dbContext;
    private readonly IEventPublisher _events;
    private readonly IOptions<PlaybackOptions> _playbackOptions;

    public TrackPreferenceService(
        AkiraDbContext dbContext,
        IEventPublisher events,
        IOptions<PlaybackOptions> playbackOptions)
    {
        _dbContext = dbContext;
        _events = events;
        _playbackOptions = playbackOptions;
    }

    /// <inheritdoc />
    public async Task<EffectiveTrackRules> GetEffectiveAsync(Guid userId, string itemKey, string? seriesKey, CancellationToken cancellationToken)
    {
        var keys = new[] { TrackPreference.GlobalKey, itemKey, seriesKey ?? itemKey };
        var rows = await _dbContext.TrackPreferences.AsNoTracking()
            .Where(p => p.UserId == userId && keys.Contains(p.ScopeKey))
            .ToListAsync(cancellationToken);

        TrackRules Rules(PreferenceScope scope, string? key)
        {
            if (key is null)
            {
                return TrackRules.None;
            }

            var row = rows.FirstOrDefault(p => p.Scope == scope && p.ScopeKey == key);
            return ToRules(row);
        }

        return EffectiveTrackRules.Combine(
            Rules(PreferenceScope.Item, itemKey),
            Rules(PreferenceScope.Series, seriesKey),
            Rules(PreferenceScope.Global, TrackPreference.GlobalKey),
            _playbackOptions.Value.Languages.ToRules());
    }

    /// <inheritdoc />
    public async Task<TrackPreferenceOverview> GetOverviewAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.TrackPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        var keys = rows.Where(p => p.Scope != PreferenceScope.Global).Select(p => p.ScopeKey).ToList();
        var items = await _dbContext.MediaItems.AsNoTracking()
            .Where(item => keys.Contains(item.IdentityKey))
            .Select(item => new { item.IdentityKey, item.Id, item.Title })
            .ToDictionaryAsync(item => item.IdentityKey, cancellationToken);

        var overrides = rows
            .Where(p => p.Scope != PreferenceScope.Global)
            .Select(p =>
            {
                var item = items.GetValueOrDefault(p.ScopeKey);
                var remembersAudio = p.RememberedAudio is not null;
                var remembersSubtitle = p.RememberedSubtitle is not null || p.RememberedSubtitleOff == true;
                return new TrackPreferenceOverride(p.Scope, item?.Id, item?.Title, ToModel(p), remembersAudio, remembersSubtitle);
            })
            .OrderBy(o => o.Title)
            .ToList();

        var global = rows.FirstOrDefault(p => p.Scope == PreferenceScope.Global);
        var server = _playbackOptions.Value.Languages.ToRules();
        var effective = EffectiveTrackRules.Combine(TrackRules.None, TrackRules.None, ToRules(global), server);
        var serverRules = new TrackRulesModel(server.AudioLanguages, server.PreferOriginalLanguage, null, null, null, server.SubtitleMode,
            server.SubtitleLanguages, null);
        return new TrackPreferenceOverview(ToModel(global), overrides, serverRules, ToModel(effective));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<TrackRulesModel>> SetGlobalAsync(Guid userId, TrackRulesModel rules, CancellationToken cancellationToken)
    {
        if (Validate(rules) is { } problem)
        {
            return ServiceResult<TrackRulesModel>.Fail(ServiceError.Invalid, problem);
        }

        var row = await GetOrAddAsync(userId, PreferenceScope.Global, TrackPreference.GlobalKey, cancellationToken);
        Apply(row, rules);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await PublishAsync(userId, PreferenceScope.Global, null);
        return ServiceResult<TrackRulesModel>.Ok(ToModel(row));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ItemTrackPreferences>> GetForItemAsync(Guid userId, Guid itemId, CancellationToken cancellationToken)
    {
        var target = await FindScopeAsync(itemId, cancellationToken);
        if (target is null)
        {
            return ServiceResult<ItemTrackPreferences>.Fail(ServiceError.NotFound, $"Item {itemId} does not exist.");
        }

        return ServiceResult<ItemTrackPreferences>.Ok(await DescribeAsync(userId, target, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ItemTrackPreferences>> SetForItemAsync(
        Guid userId,
        Guid itemId,
        TrackRulesModel rules,
        CancellationToken cancellationToken)
    {
        if (Validate(rules) is { } problem)
        {
            return ServiceResult<ItemTrackPreferences>.Fail(ServiceError.Invalid, problem);
        }

        var target = await FindScopeAsync(itemId, cancellationToken);
        if (target is null)
        {
            return ServiceResult<ItemTrackPreferences>.Fail(ServiceError.NotFound, $"Item {itemId} does not exist.");
        }

        var row = await GetOrAddAsync(userId, target.Scope, target.Key, cancellationToken);
        Apply(row, rules);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await PublishAsync(userId, target.Scope, target.Key);
        return ServiceResult<ItemTrackPreferences>.Ok(await DescribeAsync(userId, target, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> ResetForItemAsync(Guid userId, Guid itemId, CancellationToken cancellationToken)
    {
        var target = await FindScopeAsync(itemId, cancellationToken);
        if (target is null)
        {
            return ServiceResult<bool>.Fail(ServiceError.NotFound, $"Item {itemId} does not exist.");
        }

        await _dbContext.TrackPreferences
            .Where(p => p.UserId == userId && p.Scope == target.Scope && p.ScopeKey == target.Key)
            .ExecuteDeleteAsync(cancellationToken);
        await PublishAsync(userId, target.Scope, target.Key);
        return ServiceResult<bool>.Ok(true);
    }

    private Task PublishAsync(Guid userId, PreferenceScope scope, string? identityKey)
    {
        var payload = new TrackPreferencePayload(scope, identityKey);
        return _events.PublishAsync([new EventDraft(EventTypes.TrackPreferenceChanged, payload, userId)]);
    }

    // A series or season stands for the whole series, so its preferences live at the series
    private async Task<ScopeTarget?> FindScopeAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var item = await _dbContext.MediaItems.AsNoTracking()
            .Where(item => item.Id == itemId)
            .Select(item => new
            {
                item.Type,
                item.IdentityKey,
                SeriesKey = item.Type == MediaItemType.Episode ? item.Parent!.Parent!.IdentityKey
                    : item.Type == MediaItemType.Season ? item.Parent!.IdentityKey
                    : null
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return null;
        }

        return item.Type switch
        {
            MediaItemType.Series => new ScopeTarget(PreferenceScope.Series, item.IdentityKey, item.IdentityKey, null),
            MediaItemType.Season => new ScopeTarget(PreferenceScope.Series, item.SeriesKey!, item.SeriesKey!, null),
            MediaItemType.Episode => new ScopeTarget(PreferenceScope.Item, item.IdentityKey, item.IdentityKey, item.SeriesKey),
            _ => new ScopeTarget(PreferenceScope.Item, item.IdentityKey, item.IdentityKey, null)
        };
    }

    private async Task<ItemTrackPreferences> DescribeAsync(Guid userId, ScopeTarget target, CancellationToken cancellationToken)
    {
        var row = await _dbContext.TrackPreferences.AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == userId && p.Scope == target.Scope && p.ScopeKey == target.Key, cancellationToken);
        EffectiveTrackRules effective;
        if (target.Scope == PreferenceScope.Series)
        {
            effective = await GetEffectiveAsync(userId, target.Key, target.Key, cancellationToken);
        }
        else
        {
            effective = await GetEffectiveAsync(userId, target.ItemKey, target.SeriesKey, cancellationToken);
        }

        var remembersAudio = row?.RememberedAudio is not null;
        var remembersSubtitle = row?.RememberedSubtitle is not null || row?.RememberedSubtitleOff == true;
        return new ItemTrackPreferences(target.Scope, ToModel(row), ToModel(effective), remembersAudio, remembersSubtitle);
    }

    private async Task<TrackPreference> GetOrAddAsync(Guid userId, PreferenceScope scope, string key, CancellationToken cancellationToken)
    {
        var row = await _dbContext.TrackPreferences
            .SingleOrDefaultAsync(p => p.UserId == userId && p.Scope == scope && p.ScopeKey == key, cancellationToken);
        if (row is null)
        {
            row = new TrackPreference { UserId = userId, Scope = scope, ScopeKey = key };
            _dbContext.TrackPreferences.Add(row);
        }

        return row;
    }

    private static string? Validate(TrackRulesModel rules)
    {
        var languages = (rules.AudioLanguages ?? []).Concat(rules.SubtitleLanguages ?? []);
        foreach (var language in languages)
        {
            if (Languages.Normalize(language) is not { Length: 3 })
            {
                return $"'{language}' is not a language code. Use ISO 639, for example eng, jpn or nl.";
            }
        }

        if (rules.SubtitleMode is { } mode && !Enum.IsDefined(mode))
        {
            return "subtitleMode is off, forcedOnly, whenAudioForeign or always.";
        }

        var count = (rules.AudioLanguages?.Count ?? 0) + (rules.SubtitleLanguages?.Count ?? 0);
        if (count > 20)
        {
            return "At most 20 languages.";
        }

        return null;
    }

    private static void Apply(TrackPreference row, TrackRulesModel rules)
    {
        row.AudioLanguages = Join(rules.AudioLanguages);
        row.PreferOriginalLanguage = rules.PreferOriginalLanguage;
        row.PreferLossless = rules.PreferLossless;
        row.ExcludeCommentary = rules.ExcludeCommentary;
        row.ExcludeDescriptive = rules.ExcludeDescriptive;
        row.SubtitleMode = rules.SubtitleMode;
        row.SubtitleLanguages = Join(rules.SubtitleLanguages);
        row.PreferHearingImpaired = rules.PreferHearingImpaired;
    }

    // Stored normalized, so "nl" and "dut" are the same preference and match the tracks' codes
    private static string? Join(IReadOnlyList<string>? languages)
    {
        if (languages is null)
        {
            return null;
        }

        var normalized = languages.Select(language => Languages.Normalize(language)!).Distinct();
        return string.Join(',', normalized);
    }

    private static IReadOnlyList<string>? Split(string? languages) =>
        languages is null ? null : languages.Split(',', StringSplitOptions.RemoveEmptyEntries);

    private static TrackRules ToRules(TrackPreference? row)
    {
        if (row is null)
        {
            return TrackRules.None;
        }

        return new TrackRules(Split(row.AudioLanguages), row.PreferOriginalLanguage, row.PreferLossless, row.ExcludeCommentary,
            row.ExcludeDescriptive, row.SubtitleMode, Split(row.SubtitleLanguages), row.PreferHearingImpaired,
            row.RememberedAudio, row.RememberedSubtitle, row.RememberedSubtitleOff);
    }

    private static EffectiveRulesModel ToModel(EffectiveTrackRules effective) => new(
        effective.AudioLanguages, effective.PreferOriginalLanguage, effective.PreferLossless, effective.ExcludeCommentary,
        effective.ExcludeDescriptive, effective.SubtitleMode, effective.SubtitleLanguages, effective.PreferHearingImpaired);

    private static TrackRulesModel ToModel(TrackPreference? row) => new(
        Split(row?.AudioLanguages), row?.PreferOriginalLanguage, row?.PreferLossless, row?.ExcludeCommentary,
        row?.ExcludeDescriptive, row?.SubtitleMode, Split(row?.SubtitleLanguages), row?.PreferHearingImpaired);

    /// <summary>
    /// Represents where an item's track preferences are stored. Key is the series for a series or season, and SeriesKey
    /// is set only for an episode.
    /// </summary>
    private sealed record ScopeTarget(PreferenceScope Scope, string Key, string ItemKey, string? SeriesKey);
}
