using Akira.Contracts.Subtitles;
using Microsoft.EntityFrameworkCore;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Server.Events;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Interfaces.Probing;
using Akira.Infrastructure.Subtitles;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Events;

namespace Akira.Server.Services.Subtitles;

public class SubtitleRequestProcessor : ISubtitleRequestProcessor
{
    private static readonly string[] DownloadFormats = ["srt", "ass", "ssa", "vtt"];

    private readonly AkiraDbContext _dbContext;
    private readonly IEnumerable<ISubtitleSource> _sources;
    private readonly IEnumerable<ISubtitleTranslator> _translators;
    private readonly ISubtitleExtractor _extractor;
    private readonly IProbeService _probe;
    private readonly ISubtitleService _subtitles;
    private readonly IEventPublisher _events;
    private readonly ILogger<SubtitleRequestProcessor> _logger;

    public SubtitleRequestProcessor(
        AkiraDbContext dbContext,
        IEnumerable<ISubtitleSource> sources,
        IEnumerable<ISubtitleTranslator> translators,
        ISubtitleExtractor extractor,
        IProbeService probe,
        ISubtitleService subtitles,
        IEventPublisher events,
        ILogger<SubtitleRequestProcessor> logger)
    {
        _dbContext = dbContext;
        _sources = sources;
        _translators = translators;
        _extractor = extractor;
        _probe = probe;
        _subtitles = subtitles;
        _events = events;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> ProcessAsync(CancellationToken cancellationToken)
    {
        var open = await _dbContext.SubtitleRequests
            .Where(r => r.Status == SubtitleRequestStatus.Queued || r.Status == SubtitleRequestStatus.Running)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var request in open)
        {
            var before = (request.Status, request.Progress);
            try
            {
                await StepAsync(request, cancellationToken);
            }
            catch (Exception exception) when (exception is UpstreamException or ProviderException or SubtitleFileException)
            {
                var transient = exception is UpstreamException { Failure: UpstreamFailure.Timeout or UpstreamFailure.Unreachable }
                    or ProviderException { Transient: true };
                if (transient && request.Status == SubtitleRequestStatus.Running)
                {
                    // The translator is away for a moment; the job runs on there, so it is asked again next round
                    _logger.LogWarning("Could not ask {Provider} about job {Job}: {Reason}", request.ProviderKey, request.ExternalJobId, exception.Message);
                    continue;
                }

                Fail(request, exception.Message);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Left queued it would be tried again every round, and every request behind it would wait for good
                _logger.LogError(exception, "Subtitle request {Id} stopped on an error nobody expected", request.Id);
                Fail(request, "The server could not handle this request, its log says why.");
            }

            if ((request.Status, request.Progress) != before)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                await PublishAsync(request, cancellationToken);
            }
        }

        return open.Count;
    }

    private async Task StepAsync(SubtitleRequest request, CancellationToken cancellationToken)
    {
        var file = await _dbContext.MediaFiles.AsNoTracking()
            .Include(f => f.Streams)
            .Include(f => f.MediaItem)
            .SingleOrDefaultAsync(f => f.Id == request.MediaFileId, cancellationToken);
        if (file is null)
        {
            Fail(request, "The file is gone from the library.");
            return;
        }

        // Someone else asked for the same first: wait for that, then take its subtitle
        if (request.Status == SubtitleRequestStatus.Queued)
        {
            var earlier = await _dbContext.SubtitleRequests.AsNoTracking()
                .Where(r => r.Id != request.Id && r.MediaFileId == request.MediaFileId && r.Language == request.Language && r.CreatedAt <= request.CreatedAt
                            && r.Status != SubtitleRequestStatus.Failed && r.Status != SubtitleRequestStatus.Cancelled)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new { r.Status, r.ResultStreamId, r.CompletedAt })
                .FirstOrDefaultAsync(cancellationToken);
            if (earlier is { Status: SubtitleRequestStatus.Queued or SubtitleRequestStatus.Running })
            {
                return;
            }

            if (earlier is { Status: SubtitleRequestStatus.Completed, ResultStreamId: { } track } && earlier.CompletedAt >= request.CreatedAt
                && file.Streams.Any(stream => stream.Id == track))
            {
                request.Status = SubtitleRequestStatus.Completed;
                request.ResultStreamId = track;
                request.Progress = 100;
                request.CompletedAt = DateTimeOffset.UtcNow;
                return;
            }
        }

        if (request.Kind == SubtitleRequestKind.Download)
        {
            await DownloadAsync(request, file, cancellationToken);
        }
        else if (request.Status == SubtitleRequestStatus.Queued)
        {
            await StartTranslationAsync(request, file, cancellationToken);
        }
        else
        {
            await FollowTranslationAsync(request, file, cancellationToken);
        }
    }

    private async Task DownloadAsync(SubtitleRequest request, MediaFile file, CancellationToken cancellationToken)
    {
        var source = _sources.FirstOrDefault(s => s.Key == request.ProviderKey && s.IsConfigured);
        if (source is null)
        {
            Fail(request, $"{request.ProviderKey} is no longer configured.");
            return;
        }

        request.Status = SubtitleRequestStatus.Running;
        var download = await source.DownloadAsync(request.SourceReference!, cancellationToken);
        var format = download.Format.TrimStart('.').ToLowerInvariant();
        if (!DownloadFormats.Contains(format))
        {
            // The format becomes the extension of a file in the library, and a plugin may answer anything
            Fail(request, $"{source.Name} sent a {download.Format} file, which is not a subtitle Akira reads.");
            return;
        }

        var path = SubtitlePlacement.FreePath(file.Path, request.Language, format);
        await SubtitlePlacement.WriteAsync(path, download.Content, cancellationToken);
        await CompleteAsync(request, file, path, cancellationToken);
        if (request.Status == SubtitleRequestStatus.Failed)
        {
            SubtitlePlacement.TryDelete(path);
        }
    }

    private async Task StartTranslationAsync(SubtitleRequest request, MediaFile file, CancellationToken cancellationToken)
    {
        var translator = _translators.FirstOrDefault(t => t.Key == request.ProviderKey && t.IsConfigured);
        var from = file.Streams.FirstOrDefault(s => s.Id == request.SourceReference);
        if (translator is null || from?.Language is null)
        {
            var error = translator is null ? $"{request.ProviderKey} is no longer configured." : "The track to translate from is gone.";
            Fail(request, error);
            return;
        }

        string sourcePath;
        if (from is { IsExternal: true, ExternalPath: { } externalPath })
        {
            sourcePath = externalPath;
        }
        else
        {
            // Translators read files, so an embedded track is written out next to the video first and removed afterwards
            var format = SubtitleService.ExtractedFormat(from.Codec);
            sourcePath = SubtitlePlacement.FreePath(file.Path, from.Language, format);

            // Saved before the file exists, so a server stopped halfway still knows what to remove
            request.TemporaryPath = sourcePath;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _extractor.ExtractAsync(file.Path, from.IndexHint ?? 0, sourcePath, cancellationToken);
        }

        var sourceFormat = Path.GetExtension(sourcePath).TrimStart('.');
        var kind = file.MediaItem.Type == MediaItemType.Episode ? MediaKind.Episode : MediaKind.Movie;
        var job = new TranslationJob(sourcePath, from.Language, request.Language, sourceFormat, kind);
        request.ExternalJobId = await translator.StartAsync(job, cancellationToken);
        request.Status = SubtitleRequestStatus.Running;
        _logger.LogInformation("{Translator} job {Job} translates {Source} to {Language}", translator.Name, request.ExternalJobId, sourcePath,
            request.Language);
    }

    private async Task FollowTranslationAsync(SubtitleRequest request, MediaFile file, CancellationToken cancellationToken)
    {
        var translator = _translators.FirstOrDefault(t => t.Key == request.ProviderKey && t.IsConfigured);
        if (translator is null || request.ExternalJobId is null)
        {
            Fail(request, $"{request.ProviderKey} is no longer configured.");
            return;
        }

        var progress = await translator.GetAsync(request.ExternalJobId, cancellationToken);
        request.Progress = progress.Progress;
        switch (progress.Status)
        {
            case TranslationState.Completed when progress.ResultPath is not null:
                SubtitlePlacement.TryDelete(request.TemporaryPath);
                request.TemporaryPath = null;
                await CompleteAsync(request, file, progress.ResultPath, cancellationToken);
                break;
            case TranslationState.Completed:
                Fail(request, $"{translator.Name} finished without saying where it wrote the subtitle.");
                break;
            case TranslationState.Failed or TranslationState.Cancelled:
                Fail(request, progress.Error ?? $"{translator.Name} stopped the job.");
                break;
        }
    }

    private async Task CompleteAsync(SubtitleRequest request, MediaFile file, string path, CancellationToken cancellationToken)
    {
        // Read back like any subtitle next to the video, so it gets its stable id and the track rules apply to it
        var tracks = await _probe.RescanSubtitlesAsync(file.Id, cancellationToken);
        var track = tracks.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
        if (track is null)
        {
            Fail(request, $"The subtitle was written to {path}, but is not a subtitle file next to the video. Check the path mappings.");
            return;
        }

        request.Status = SubtitleRequestStatus.Completed;
        request.Progress = 100;
        request.ResultPath = path;
        request.ResultStreamId = track.StreamId;
        request.CompletedAt = DateTimeOffset.UtcNow;
        var payload = new LibraryItemPayload(file.MediaItemId, file.MediaItem.Type, file.MediaItem.IdentityKey, file.MediaItem.ParentId, null);
        await _events.PublishAsync([new EventDraft(EventTypes.ItemUpdated, payload)]);
        _logger.LogInformation("{Language} subtitle of {Title} is ready: {Path}", request.Language, file.MediaItem.Title, path);
    }

    private void Fail(SubtitleRequest request, string error)
    {
        SubtitlePlacement.TryDelete(request.TemporaryPath);
        request.TemporaryPath = null;
        request.Status = SubtitleRequestStatus.Failed;
        request.Error = error;
        request.CompletedAt = DateTimeOffset.UtcNow;
        _logger.LogWarning("Subtitle request {Id} failed: {Reason}", request.Id, error);
    }

    private async Task PublishAsync(SubtitleRequest request, CancellationToken cancellationToken)
    {
        var model = await _subtitles.ToModelAsync(request, cancellationToken);
        await _events.PublishAsync([new EventDraft(EventTypes.SubtitleRequestUpdated, model, request.UserId)]);
    }
}
