using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Subtitles;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Files;

namespace Akira.Server.Services;

public class FileService : IFileService
{
    private readonly AkiraDbContext _dbContext;
    private readonly IOptions<ArrOptions> _options;
    private readonly ISubtitleConverter _converter;

    public FileService(
        AkiraDbContext dbContext,
        IOptions<ArrOptions> options,
        ISubtitleConverter converter)
    {
        _dbContext = dbContext;
        _options = options;
        _converter = converter;
    }

    /// <inheritdoc />
    public async Task<FileDetail?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var file = await _dbContext.MediaFiles.AsNoTracking()
            .Include(file => file.Streams)
            .Include(file => file.Chapters)
            .SingleOrDefaultAsync(file => file.Id == id, cancellationToken);

        if (file is null)
        {
            return null;
        }

        var instanceName = _options.Value.Instances.FirstOrDefault(instance => instance.Key == file.InstanceKey)?.DisplayName
            ?? file.InstanceKey;

        // The ffprobe index is left out on purpose, clients only get it as a hint in a playback plan
        var streams = file.Streams
            .OrderBy(stream => stream.SortOrder)
            .Select(stream => new StreamSummary(
                stream.Id, stream.Kind, stream.Codec, stream.Profile, stream.Language, stream.Title, stream.Channels,
                stream.ChannelLayout, stream.BitDepth, stream.Width, stream.Height, stream.HdrFormat, stream.FrameRate,
                stream.IsDefault, stream.IsForced, stream.IsHearingImpaired, stream.IsCommentary, stream.IsDescriptive,
                stream.IsTextBased, stream.IsExternal, stream.EventCount))
            .ToList();

        var chapters = file.Chapters
            .OrderBy(chapter => chapter.SortOrder)
            .Select(chapter => new ChapterSummary(chapter.Title, chapter.StartMs, chapter.EndMs))
            .ToList();

        return new FileDetail(file.Id, file.MediaItemId, file.InstanceKey, instanceName, file.SizeBytes, file.Container,
            file.DurationMs, file.ProbeStatus, file.ProbeError, file.ProbedAt, streams, chapters);
    }

    /// <inheritdoc />
    public async Task<FileDelivery> DeliverVideoAsync(Guid id, CancellationToken cancellationToken)
    {
        // Only paths the library knows are ever served, never one built from the request
        var file = await _dbContext.MediaFiles.AsNoTracking()
            .Where(file => file.Id == id)
            .Select(file => new { file.Path, file.Container, file.InstanceKey })
            .SingleOrDefaultAsync(cancellationToken);
        if (file is null)
        {
            return new FileDelivery(DeliveryStatus.NotFound);
        }

        var info = new FileInfo(file.Path);
        if (!info.Exists)
        {
            return new FileDelivery(DeliveryStatus.NotOnDisk, Detail: file.InstanceKey);
        }

        // With these a player resuming a download gets If-Range and If-None-Match answered, and a replaced file is
        // never stitched to the old one
        var lastModified = new DateTimeOffset(info.LastWriteTimeUtc);
        return new FileDelivery(DeliveryStatus.Ready, file.Path, VideoContentType(file.Container))
        {
            Length = info.Length,
            LastModified = lastModified,
            EntityTag = $"\"{id:N}-{info.Length:x}-{lastModified.ToUnixTimeSeconds():x}\""
        };
    }

    /// <inheritdoc />
    public async Task<FileDelivery> DeliverSubtitleAsync(Guid id, string streamId, CancellationToken cancellationToken)
    {
        var subtitle = await _dbContext.MediaStreams.AsNoTracking()
            .Where(stream => stream.MediaFileId == id && stream.Id == streamId && stream.IsExternal && stream.ExternalPath != null)
            .Select(stream => new { stream.ExternalPath, stream.MediaFile.InstanceKey })
            .SingleOrDefaultAsync(cancellationToken);
        if (subtitle is null)
        {
            return new FileDelivery(DeliveryStatus.NotFound);
        }

        return OnDisk(subtitle.ExternalPath!, SubtitleContentType(Path.GetExtension(subtitle.ExternalPath!)), subtitle.InstanceKey);
    }

    /// <inheritdoc />
    public async Task<FileDelivery> DeliverWebVttAsync(Guid id, string streamId, CancellationToken cancellationToken)
    {
        var subtitle = await _dbContext.MediaStreams.AsNoTracking()
            .Where(stream => stream.MediaFileId == id && stream.Id == streamId && stream.Kind == StreamKind.Subtitle && stream.IsTextBased)
            .Select(stream => new
            {
                stream.Codec, stream.Language, stream.IsExternal, stream.ExternalPath, stream.IndexHint, stream.MediaFile.Path,
                stream.MediaFile.InstanceKey
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (subtitle is null || (!subtitle.IsExternal && subtitle.IndexHint is null))
        {
            return new FileDelivery(DeliveryStatus.NotFound);
        }

        try
        {
            if (subtitle.IsExternal)
            {
                var external = new SubtitleSource($"{id:N}-{streamId}", subtitle.ExternalPath!, null, subtitle.Codec, subtitle.Language);
                return OnDisk(await _converter.WebVttAsync(external, [], cancellationToken), "text/vtt", subtitle.InstanceKey);
            }

            // ffmpeg reads the whole video for one embedded track, so it takes the others along
            var embedded = await _dbContext.MediaStreams.AsNoTracking()
                .Where(stream => stream.MediaFileId == id && stream.Kind == StreamKind.Subtitle && stream.IsTextBased && !stream.IsExternal
                                 && stream.IndexHint != null)
                .Select(stream => new { stream.Id, stream.IndexHint, stream.Codec, stream.Language })
                .ToListAsync(cancellationToken);
            var sources = embedded
                .Select(stream => new SubtitleSource($"{id:N}-{stream.Id}", subtitle.Path, stream.IndexHint, stream.Codec, stream.Language))
                .ToList();
            var source = sources.Single(track => track.StreamIndex == subtitle.IndexHint);
            return OnDisk(await _converter.WebVttAsync(source, sources, cancellationToken), "text/vtt", subtitle.InstanceKey);
        }
        catch (SubtitleFileException exception)
        {
            return new FileDelivery(DeliveryStatus.Failed, Detail: exception.Message);
        }
    }

    /// <summary>
    /// Returns the type a video file is sent with, by its container. Every answer with a file takes it from here: the API's,
    /// a signed url's and a bridge's listing.
    /// </summary>
    public static string VideoContentType(string? container) => container switch
    {
        "mkv" => "video/x-matroska",
        "mp4" or "m4v" => "video/mp4",
        "webm" => "video/webm",
        "avi" => "video/x-msvideo",
        // DLNA's type for MPEG-TS in packets of 188 bytes, which renderers hold a server to; the progressive stream goes out so too
        "ts" => "video/mpeg",
        "m2ts" => "video/mp2t",
        "mov" => "video/quicktime",
        _ => "application/octet-stream"
    };

    private static FileDelivery OnDisk(string path, string contentType, string instanceKey) =>
        File.Exists(path)
            ? new FileDelivery(DeliveryStatus.Ready, path, contentType)
            : new FileDelivery(DeliveryStatus.NotOnDisk, Detail: instanceKey);

    private static string SubtitleContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".srt" => "application/x-subrip",
        ".ass" or ".ssa" => "text/x-ssa",
        ".vtt" => "text/vtt",
        _ => "application/octet-stream"
    };
}
