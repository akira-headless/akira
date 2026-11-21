using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Playback;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Trickplay;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Playback;
using Akira.Server.Playback;

namespace Akira.Server.Services.Playback;

public class TrickplayService : ITrickplayService
{
    // A file that failed is tried again after this, in case it was missing and is back
    private static readonly TimeSpan RetryFailed = TimeSpan.FromDays(1);

    private readonly AkiraDbContext _dbContext;
    private readonly ITrickplayGenerator _generator;
    private readonly IOptions<TrickplayOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<TrickplayService> _logger;

    public TrickplayService(
        AkiraDbContext dbContext,
        ITrickplayGenerator generator,
        IOptions<TrickplayOptions> options,
        TimeProvider time,
        ILogger<TrickplayService> logger)
    {
        _dbContext = dbContext;
        _generator = generator;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TrickplayModel?> GetAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var layout = await ReadyLayoutAsync(fileId, cancellationToken);
        if (layout is null)
        {
            return null;
        }

        var sheets = Enumerable.Range(0, layout.SheetCount).Select(sheet => StreamRoutes.TrickplaySheetUrl(fileId, sheet)).ToList();
        return new TrickplayModel(layout.IntervalMs, layout.Width, layout.Height, layout.Columns, layout.Rows, layout.Count, sheets,
            StreamRoutes.TrickplayVttUrl(fileId));
    }

    /// <inheritdoc />
    public async Task<string?> GetVttAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var layout = await ReadyLayoutAsync(fileId, cancellationToken);
        return layout?.WebVtt(sheet => StreamRoutes.TrickplaySheetUrl(fileId, sheet));
    }

    /// <inheritdoc />
    public async Task<string?> GetSheetPathAsync(Guid fileId, int sheet, CancellationToken cancellationToken)
    {
        var layout = await ReadyLayoutAsync(fileId, cancellationToken);
        if (layout is null || sheet < 0 || sheet >= layout.SheetCount)
        {
            return null;
        }

        var path = Path.Combine(Folder(fileId), $"{sheet}.jpg");
        return File.Exists(path) ? path : null;
    }

    /// <inheritdoc />
    public async Task<bool> GenerateNextAsync(CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        var intervalMs = (int)settings.Interval.TotalMilliseconds;
        var retryBefore = _time.GetUtcNow() - RetryFailed;

        // Newest files first, they are the ones about to be watched
        var file = await _dbContext.MediaFiles.AsNoTracking()
            .Where(f => f.ProbeStatus == ProbeStatus.Probed && f.DurationMs != null && f.Streams.Any(s => s.Kind == StreamKind.Video))
            .Where(f => !_dbContext.TrickplaySets.Any(t => t.MediaFileId == f.Id
                                                          && t.Version == TrickplayGenerator.Version
                                                          && t.IntervalMs == intervalMs && t.Width == settings.Width
                                                          && t.TileColumns == settings.Columns && t.TileRows == settings.Rows
                                                          && (t.Status == TrickplayStatus.Ready || t.GeneratedAt > retryBefore)))
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new
            {
                f.Id,
                f.Path,
                f.DurationMs,
                Video = f.Streams.Where(s => s.Kind == StreamKind.Video).OrderBy(s => s.SortOrder).Select(s => new { s.Width, s.Height }).First()
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (file is null)
        {
            return false;
        }

        var row = await _dbContext.TrickplaySets.SingleOrDefaultAsync(t => t.MediaFileId == file.Id, cancellationToken);
        if (row is null)
        {
            row = new TrickplaySet { MediaFileId = file.Id };
            _dbContext.TrickplaySets.Add(row);
        }

        var started = _time.GetTimestamp();
        try
        {
            if (!File.Exists(file.Path))
            {
                throw new TrickplayException(TrickplayFailure.Failed, "The file is not on disk where the library says it is.");
            }

            var folder = Folder(file.Id);
            var layout = await _generator.GenerateAsync(file.Path, file.Video.Width, file.Video.Height, file.DurationMs!.Value, folder,
                cancellationToken);
            row.Status = TrickplayStatus.Ready;
            row.Error = null;
            row.IntervalMs = layout.IntervalMs;
            row.Width = layout.Width;
            row.Height = layout.Height;
            row.TileColumns = layout.Columns;
            row.TileRows = layout.Rows;
            row.ThumbnailCount = layout.Count;
            _logger.LogInformation("Made {Count} thumbnails on {Sheets} sheets for file {FileId} in {Elapsed}", layout.Count, layout.SheetCount,
                file.Id, _time.GetElapsedTime(started));
        }
        catch (TrickplayException exception) when (exception.Failure == TrickplayFailure.Failed)
        {
            // Kept with these settings so the same file is not tried again until the retry time
            row.Status = TrickplayStatus.Failed;
            row.Error = exception.Message;
            row.IntervalMs = intervalMs;
            row.Width = settings.Width;
            row.TileColumns = settings.Columns;
            row.TileRows = settings.Rows;
            _logger.LogWarning("No thumbnails for file {FileId}: {Reason}", file.Id, exception.Message);
        }

        row.Version = TrickplayGenerator.Version;
        row.GeneratedAt = _time.GetUtcNow();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task RemoveOrphansAsync(CancellationToken cancellationToken)
    {
        var root = _options.Value.ResolvePath();
        if (!Directory.Exists(root))
        {
            return;
        }

        var knownIds = await _dbContext.TrickplaySets.AsNoTracking().Select(t => t.MediaFileId).ToListAsync(cancellationToken);
        var known = knownIds.ToHashSet();
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            // Only folders named after a file id are this server's; a removed file's row went with it
            if (Guid.TryParseExact(Path.GetFileName(folder), "N", out var fileId) && !known.Contains(fileId))
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning("Could not remove the thumbnails in {Folder}: {Message}", folder, exception.Message);
                }
            }
        }
    }

    private string Folder(Guid fileId) => Path.Combine(_options.Value.ResolvePath(), fileId.ToString("N"));

    private async Task<TrickplayLayout?> ReadyLayoutAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.TrickplaySets.AsNoTracking()
            .SingleOrDefaultAsync(t => t.MediaFileId == fileId && t.Status == TrickplayStatus.Ready, cancellationToken);
        return row is null ? null : new TrickplayLayout(row.IntervalMs, row.Width, row.Height, row.TileColumns, row.TileRows, row.ThumbnailCount);
    }
}
