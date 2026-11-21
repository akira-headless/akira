using System.Security.Claims;
using Akira.Contracts.Subtitles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Server.Authentication;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Media;
using Akira.Server.Events;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Subtitles;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Playback;
using Akira.Server.Models.Subtitles;
using Akira.Server.Playback;
using Akira.Server.Services.Playback;

namespace Akira.Server.Services.Subtitles;

public class SubtitleService : ISubtitleService
{
    private static readonly SubtitleRequestStatus[] Active = [SubtitleRequestStatus.Queued, SubtitleRequestStatus.Running];

    private readonly AkiraDbContext _dbContext;
    private readonly IEnumerable<ISubtitleSource> _sources;
    private readonly IEnumerable<ISubtitleTranslator> _translators;
    private readonly IEventPublisher _events;
    private readonly SubtitleRequestTrigger _trigger;
    private readonly IOptions<PlaybackOptions> _playbackOptions;
    private readonly ILogger<SubtitleService> _logger;

    public SubtitleService(
        AkiraDbContext dbContext,
        IEnumerable<ISubtitleSource> sources,
        IEnumerable<ISubtitleTranslator> translators,
        IEventPublisher events,
        SubtitleRequestTrigger trigger,
        IOptions<PlaybackOptions> playbackOptions,
        ILogger<SubtitleService> logger)
    {
        _dbContext = dbContext;
        _sources = sources;
        _translators = translators;
        _events = events;
        _trigger = trigger;
        _playbackOptions = playbackOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<SubtitleProviderModel> Providers()
    {
        var sources = _sources.Select(source => new SubtitleProviderModel(source.Key, source.Name, "source", source.IsConfigured));
        var translators = _translators.Select(translator =>
            new SubtitleProviderModel(translator.Key, translator.Name, "translator", translator.IsConfigured));
        return sources.Concat(translators).ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<SubtitleOptionsResponse>> GetOptionsAsync(Guid itemId, string? language, Guid? fileId,
        CancellationToken cancellationToken)
    {
        var target = await ResolveAsync(itemId, language, fileId, cancellationToken);
        if (!target.Succeeded)
        {
            return ServiceResult<SubtitleOptionsResponse>.Fail(target.Error!.Value, target.Detail!);
        }

        var (item, file, code) = target.Value!;
        var problems = new List<string>();
        var existing = file.Streams
            .Where(stream => stream.Kind == StreamKind.Subtitle && stream.Language == code)
            .OrderBy(stream => stream.SortOrder)
            .Select(stream => new ExistingSubtitle(OptionIds.Existing(stream.Id), stream.Id, stream.Title, stream.Codec, stream.IsForced,
                stream.IsHearingImpaired, stream.IsExternal))
            .ToList();

        var downloads = new List<DownloadOption>();
        var search = await SearchFor(item, file, code, cancellationToken);
        foreach (var source in _sources.Where(source => source.IsConfigured))
        {
            try
            {
                var candidates = await source.SearchAsync(search, cancellationToken);
                var options = candidates.Take(25).Select(candidate => new DownloadOption(
                    OptionIds.Download(source.Key, candidate.Reference), source.Key, source.Name, candidate.Release, candidate.Downloads,
                    candidate.HearingImpaired, candidate.Forced, candidate.HashMatch));
                downloads.AddRange(options);
            }
            catch (Exception exception) when (exception is UpstreamException or ProviderException)
            {
                // One source failing still leaves the others and the translations
                problems.Add(exception.Message);
            }
        }

        var translations = new List<TranslationOption>();
        foreach (var translator in _translators.Where(translator => translator.IsConfigured))
        {
            translations.AddRange(TranslationSources(file, code).Select(stream =>
            {
                var (translatable, reason) = CanTranslate(stream, translator);
                return new TranslationOption(OptionIds.Translate(translator.Key, stream.Id), translator.Key, translator.Name, stream.Id,
                    stream.Language!, stream.Title, stream.IsExternal, translatable, reason);
            }));
        }

        var response = new SubtitleOptionsResponse(item.Id, file.Id, code, existing, downloads, translations, problems);
        return ServiceResult<SubtitleOptionsResponse>.Ok(response);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<SubtitleRequestModel>> CreateAsync(ClaimsPrincipal user, Guid itemId, CreateSubtitleRequest request,
        CancellationToken cancellationToken)
    {
        var target = await ResolveAsync(itemId, request.Language, request.FileId, cancellationToken);
        if (!target.Succeeded)
        {
            return ServiceResult<SubtitleRequestModel>.Fail(target.Error!.Value, target.Detail!);
        }

        var (item, file, code) = target.Value!;
        var userId = user.GetUserId()!.Value;

        // Asking twice, or from two devices, follows the user's own request that is already under way; another user's
        // request for the same gets one of its own, which the processor finishes with that result rather than doing it twice
        var running = await _dbContext.SubtitleRequests
            .Where(r => r.UserId == userId && r.MediaFileId == file.Id && r.Language == code && Active.Contains(r.Status))
            .OrderBy(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (running is not null)
        {
            return ServiceResult<SubtitleRequestModel>.Ok(await ToModelAsync(running, cancellationToken));
        }

        ServiceResult<(SubtitleRequestKind, string, string)> choice;
        if (request.OptionId is null)
        {
            choice = await DecideAsync(item, file, code, cancellationToken);
        }
        else
        {
            choice = Parse(request.OptionId, file, code);
        }

        if (!choice.Succeeded)
        {
            return ServiceResult<SubtitleRequestModel>.Fail(choice.Error!.Value, choice.Detail!);
        }

        var (kind, provider, reference) = choice.Value!;
        var entity = new SubtitleRequest
        {
            UserId = userId,
            MediaItemId = item.Id,
            MediaFileId = file.Id,
            Language = code,
            Kind = kind,
            ProviderKey = provider,
            SourceReference = reference,
            SessionId = request.SessionId,
            Status = kind == SubtitleRequestKind.Existing ? SubtitleRequestStatus.Completed : SubtitleRequestStatus.Queued,
            ResultStreamId = kind == SubtitleRequestKind.Existing ? reference : null,
            Progress = kind == SubtitleRequestKind.Existing ? 100 : 0
        };
        if (kind == SubtitleRequestKind.Existing)
        {
            entity.CompletedAt = DateTimeOffset.UtcNow;
        }

        _dbContext.SubtitleRequests.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("{User} asked for {Language} subtitles of {Title}: {Kind} with {Provider}", user.FindFirstValue(AkiraClaims.UserName),
            code, item.Title, kind, provider);

        var model = await ToModelAsync(entity, cancellationToken);
        await _events.PublishAsync([new EventDraft(EventTypes.SubtitleRequestUpdated, model, userId)]);
        if (kind != SubtitleRequestKind.Existing)
        {
            _trigger.Request();
        }

        return ServiceResult<SubtitleRequestModel>.Ok(model);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<SubtitleRequestModel>> GetAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken)
    {
        var request = await FindAsync(user, id, cancellationToken);
        if (request is null)
        {
            return ServiceResult<SubtitleRequestModel>.Fail(ServiceError.NotFound, $"Subtitle request {id} does not exist.");
        }

        var model = await ToModelAsync(request, cancellationToken);
        return ServiceResult<SubtitleRequestModel>.Ok(model);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubtitleRequestModel>> ListAsync(ClaimsPrincipal user, Guid? itemId, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var query = _dbContext.SubtitleRequests.AsNoTracking().Where(r => user.IsAdmin() || r.UserId == userId);
        if (itemId is not null)
        {
            query = query.Where(r => r.MediaItemId == itemId);
        }

        var rows = await query.OrderByDescending(r => r.CreatedAt).Take(50).ToListAsync(cancellationToken);
        var models = new List<SubtitleRequestModel>();
        foreach (var row in rows)
        {
            models.Add(await ToModelAsync(row, cancellationToken));
        }

        return models;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<SubtitleRequestModel>> CancelAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken)
    {
        var request = await FindAsync(user, id, cancellationToken);
        if (request is null)
        {
            return ServiceResult<SubtitleRequestModel>.Fail(ServiceError.NotFound, $"Subtitle request {id} does not exist.");
        }

        if (!Active.Contains(request.Status))
        {
            return ServiceResult<SubtitleRequestModel>.Fail(ServiceError.Conflict, $"The request is {request.Status.ToString().ToLowerInvariant()} already.");
        }

        if (request.ExternalJobId is not null && _translators.FirstOrDefault(t => t.Key == request.ProviderKey) is { IsConfigured: true } translator)
        {
            try
            {
                await translator.CancelAsync(request.ExternalJobId, cancellationToken);
            }
            catch (Exception exception) when (exception is UpstreamException or ProviderException)
            {
                _logger.LogWarning("Could not cancel {Provider} job {Job}: {Reason}", translator.Name, request.ExternalJobId, exception.Message);
            }
        }

        SubtitlePlacement.TryDelete(request.TemporaryPath);
        request.Status = SubtitleRequestStatus.Cancelled;
        request.CompletedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var model = await ToModelAsync(request, cancellationToken);
        await _events.PublishAsync([new EventDraft(EventTypes.SubtitleRequestUpdated, model, request.UserId)]);
        return ServiceResult<SubtitleRequestModel>.Ok(model);
    }

    /// <inheritdoc />
    public async Task<SubtitleRequestModel> ToModelAsync(SubtitleRequest request, CancellationToken cancellationToken)
    {
        PlanTrack? track = null;
        if (request is { Status: SubtitleRequestStatus.Completed, ResultStreamId: { } streamId })
        {
            var stream = await _dbContext.MediaStreams.AsNoTracking()
                .SingleOrDefaultAsync(s => s.MediaFileId == request.MediaFileId && s.Id == streamId, cancellationToken);
            if (stream is not null)
            {
                var clientPaths = _playbackOptions.Value.CreateClientPathMapper();
                var match = PlaybackService.Match(stream, request.MediaFileId, clientPaths);
                track = new PlanTrack(stream.Id, stream.Kind, match);
            }
        }

        return new SubtitleRequestModel(request.Id, request.MediaItemId, request.MediaFileId, request.Language, request.Kind, request.ProviderKey,
            request.Status, request.Progress, request.ResultStreamId, request.Error, request.SessionId, request.CreatedAt, request.CompletedAt, track);
    }

    private Task<SubtitleRequest?> FindAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken)
    {
        // Someone else's request is answered as one that does not exist, admins see all of them
        var userId = user.GetUserId();
        return _dbContext.SubtitleRequests.SingleOrDefaultAsync(r => r.Id == id && (user.IsAdmin() || r.UserId == userId), cancellationToken);
    }

    private async Task<ServiceResult<(MediaItem Item, MediaFile File, string Language)>> ResolveAsync(Guid itemId, string? language, Guid? fileId,
        CancellationToken cancellationToken)
    {
        var code = Languages.Normalize(language);
        if (code is null)
        {
            return ServiceResult<(MediaItem, MediaFile, string)>.Fail(ServiceError.Invalid,
                "language is required, as ISO 639-1 or 639-2, for example nl or dut.");
        }

        var item = await _dbContext.MediaItems.AsNoTracking()
            .Include(i => i.Parent).ThenInclude(season => season!.Parent)
            .SingleOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null)
        {
            return ServiceResult<(MediaItem, MediaFile, string)>.Fail(ServiceError.NotFound, $"Item {itemId} does not exist.");
        }

        if (item.Type is not (MediaItemType.Movie or MediaItemType.Episode))
        {
            return ServiceResult<(MediaItem, MediaFile, string)>.Fail(ServiceError.Invalid, "Subtitles are asked for per movie or episode.");
        }

        var files = await _dbContext.MediaFiles.AsNoTracking()
            .Include(f => f.Streams)
            .Where(f => f.MediaItemId == item.Id)
            .ToListAsync(cancellationToken);
        MediaFile? file;
        if (fileId is null)
        {
            file = files.OrderBy(f => f.CreatedAt).FirstOrDefault();
        }
        else
        {
            file = files.FirstOrDefault(f => f.Id == fileId);
        }

        if (file is null)
        {
            if (fileId is null)
            {
                return ServiceResult<(MediaItem, MediaFile, string)>.Fail(ServiceError.Conflict, $"{item.Title} has no file.");
            }

            return ServiceResult<(MediaItem, MediaFile, string)>.Fail(ServiceError.NotFound, $"File {fileId} is not a version of {item.Title}.");
        }

        return ServiceResult<(MediaItem, MediaFile, string)>.Ok((item, file, code));
    }

    private async Task<ServiceResult<(SubtitleRequestKind, string, string)>> DecideAsync(MediaItem item, MediaFile file, string code,
        CancellationToken cancellationToken)
    {
        // The cheapest that works: a track it has, then a download, then a translation
        var present = file.Streams
            .Where(s => s.Kind == StreamKind.Subtitle && s.Language == code && !s.IsForced)
            .OrderBy(s => s.SortOrder)
            .FirstOrDefault();
        if (present is not null)
        {
            return ServiceResult<(SubtitleRequestKind, string, string)>.Ok((SubtitleRequestKind.Existing, "existing", present.Id));
        }

        var search = await SearchFor(item, file, code, cancellationToken);
        foreach (var source in _sources.Where(source => source.IsConfigured))
        {
            try
            {
                var candidates = await source.SearchAsync(search, cancellationToken);
                var best = candidates.FirstOrDefault(c => !c.Forced);
                if (best is not null)
                {
                    return ServiceResult<(SubtitleRequestKind, string, string)>.Ok((SubtitleRequestKind.Download, source.Key, best.Reference));
                }
            }
            catch (Exception exception) when (exception is UpstreamException or ProviderException)
            {
                _logger.LogWarning("{Source} search failed, trying the next option: {Reason}", source.Name, exception.Message);
            }
        }

        foreach (var translator in _translators.Where(translator => translator.IsConfigured))
        {
            var translateFrom = TranslationSources(file, code).FirstOrDefault(s => CanTranslate(s, translator).Translatable);
            if (translateFrom is not null)
            {
                return ServiceResult<(SubtitleRequestKind, string, string)>.Ok((SubtitleRequestKind.Translate, translator.Key, translateFrom.Id));
            }
        }

        var anyConfigured = _sources.Any(s => s.IsConfigured) || _translators.Any(t => t.IsConfigured);
        var detail = anyConfigured
            ? $"No {code} subtitle was found for {item.Title}, and it has no text subtitle to translate from."
            : $"No subtitle source or translator is configured, see {SubtitleOptions.SectionName} in the server settings.";
        return ServiceResult<(SubtitleRequestKind, string, string)>.Fail(ServiceError.Conflict, detail);
    }

    private ServiceResult<(SubtitleRequestKind, string, string)> Parse(string optionId, MediaFile file, string code)
    {
        var option = OptionIds.Parse(optionId);
        if (option is null)
        {
            return ServiceResult<(SubtitleRequestKind, string, string)>.Fail(ServiceError.Invalid, $"optionId {optionId} is not one the options listed.");
        }

        var (kind, provider, reference) = option.Value;
        switch (kind)
        {
            case SubtitleRequestKind.Existing when file.Streams.Any(s => s.Id == reference && s.Kind == StreamKind.Subtitle):
                return ServiceResult<(SubtitleRequestKind, string, string)>.Ok((kind, "existing", reference));
            case SubtitleRequestKind.Download when _sources.Any(s => s.Key == provider && s.IsConfigured):
                return ServiceResult<(SubtitleRequestKind, string, string)>.Ok((kind, provider, reference));
            case SubtitleRequestKind.Translate when CanTranslateOption(provider, reference, file, code):
                return ServiceResult<(SubtitleRequestKind, string, string)>.Ok((kind, provider, reference));
            default:
                return ServiceResult<(SubtitleRequestKind, string, string)>.Fail(ServiceError.Invalid,
                    $"optionId {optionId} does not fit this file, or its provider is not configured.");
        }
    }

    private bool CanTranslateOption(string provider, string reference, MediaFile file, string code)
    {
        var translator = _translators.FirstOrDefault(t => t.Key == provider && t.IsConfigured);
        if (translator is null)
        {
            return false;
        }

        var source = file.Streams.FirstOrDefault(s => s.Id == reference);
        if (source is null)
        {
            return false;
        }

        return source.Language != code && CanTranslate(source, translator).Translatable;
    }

    private static IEnumerable<MediaStream> TranslationSources(MediaFile file, string code) =>
        file.Streams
            .Where(s => s.Kind == StreamKind.Subtitle && s.Language is not null && s.Language != code && !s.IsForced)
            // A file next to the video first, it needs no extracting; then the fullest track
            .OrderByDescending(s => s.IsExternal)
            .ThenByDescending(s => s.IsTextBased)
            .ThenByDescending(s => s.EventCount ?? 0)
            .ThenBy(s => s.SortOrder);

    private static (bool Translatable, string? Reason) CanTranslate(MediaStream stream, ISubtitleTranslator translator)
    {
        if (!stream.IsTextBased)
        {
            return (false, "An image based subtitle has no text to translate.");
        }

        // An embedded text track is extracted to SRT or ASS first; an external file goes as it is
        string format;
        if (stream.IsExternal)
        {
            format = Path.GetExtension(stream.ExternalPath ?? "").TrimStart('.');
        }
        else
        {
            format = ExtractedFormat(stream.Codec);
        }

        if (!translator.Formats.Contains(format))
        {
            return (false, $"{translator.Name} does not read {format} subtitles.");
        }

        return (true, null);
    }

    internal static string ExtractedFormat(string? codec) => codec is "ass" or "ssa" ? "ass" : "srt";

    private static async Task<SubtitleSearch> SearchFor(MediaItem item, MediaFile file, string code, CancellationToken cancellationToken)
    {
        string? hash = null;
        try
        {
            if (File.Exists(file.Path))
            {
                hash = await FileHash.ComputeAsync(file.Path, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Without the hash the search still finds subtitles, just not ones timed for exactly this file
        }

        var series = item.Type == MediaItemType.Episode ? item.Parent?.Parent : null;
        var kind = item.Type == MediaItemType.Episode ? MediaKind.Episode : MediaKind.Movie;
        return new SubtitleSearch(code, kind, item.TmdbId, item.ImdbId, series?.TmdbId, series?.ImdbId, item.SeasonNumber, item.EpisodeNumber, hash);

    }
}
