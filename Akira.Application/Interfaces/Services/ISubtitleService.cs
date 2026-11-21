using System.Security.Claims;
using Akira.Core.Entities;
using Akira.Server.Models.Subtitles;
using Akira.Server.Services;

namespace Akira.Server.Interfaces.Services;

public interface ISubtitleService
{
    /// <summary>
    /// Lists the subtitle sources and translators and whether each is configured.
    /// </summary>
    IReadOnlyList<SubtitleProviderModel> Providers();

    /// <summary>
    /// Lists what can be done for a language: the tracks a version has, downloads the sources found, and tracks to translate from.
    /// </summary>
    Task<ServiceResult<SubtitleOptionsResponse>> GetOptionsAsync(Guid itemId, string? language, Guid? fileId, CancellationToken cancellationToken);

    /// <summary>
    /// Asks for a subtitle, with a chosen option or the best one, and queues the work. An open request for the same file and language is returned instead.
    /// </summary>
    Task<ServiceResult<SubtitleRequestModel>> CreateAsync(ClaimsPrincipal user, Guid itemId, CreateSubtitleRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a request of the user, or any request for an admin.
    /// </summary>
    Task<ServiceResult<SubtitleRequestModel>> GetAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the user's latest requests, or everyone's for an admin, optionally for one item.
    /// </summary>
    Task<IReadOnlyList<SubtitleRequestModel>> ListAsync(ClaimsPrincipal user, Guid? itemId, CancellationToken cancellationToken);

    /// <summary>
    /// Stops a queued or running request, and the translator's job with it.
    /// </summary>
    Task<ServiceResult<SubtitleRequestModel>> CancelAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Describes a request as clients see it, with the new track once it is there.
    /// </summary>
    Task<SubtitleRequestModel> ToModelAsync(SubtitleRequest request, CancellationToken cancellationToken);
}
