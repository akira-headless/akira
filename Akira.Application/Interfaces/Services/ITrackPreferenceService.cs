using Akira.Core.Playback;
using Akira.Server.Models.Playback;
using Akira.Server.Services;

namespace Akira.Server.Interfaces.Services;

public interface ITrackPreferenceService
{
    /// <summary>
    /// Combines the item, series and global rules of a user for one item.
    /// </summary>
    Task<EffectiveTrackRules> GetEffectiveAsync(Guid userId, string itemKey, string? seriesKey, CancellationToken cancellationToken);

    Task<TrackPreferenceOverview> GetOverviewAsync(Guid userId, CancellationToken cancellationToken);

    Task<ServiceResult<TrackRulesModel>> SetGlobalAsync(Guid userId, TrackRulesModel rules, CancellationToken cancellationToken);

    Task<ServiceResult<ItemTrackPreferences>> GetForItemAsync(Guid userId, Guid itemId, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the rules for a movie or episode, or for the whole series when given a series or season.
    /// </summary>
    Task<ServiceResult<ItemTrackPreferences>> SetForItemAsync(Guid userId, Guid itemId, TrackRulesModel rules, CancellationToken cancellationToken);

    /// <summary>
    /// Forgets the rules and remembered tracks of that scope, so the global rules apply again.
    /// </summary>
    Task<ServiceResult<bool>> ResetForItemAsync(Guid userId, Guid itemId, CancellationToken cancellationToken);
}
