using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Library;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Images;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Interfaces.Integrations;
using Akira.Server.Interfaces.Services;

namespace Akira.Server.Services;

public class ImageService : IImageService
{
    private readonly AkiraDbContext _dbContext;
    private readonly IArrClientFactory _clients;
    private readonly IArtworkClient _artwork;
    private readonly ImageCache _cache;
    private readonly IOptions<ArrOptions> _options;
    private readonly ILogger<ImageService> _logger;

    public ImageService(
        AkiraDbContext dbContext,
        IArrClientFactory clients,
        IArtworkClient artwork,
        ImageCache cache,
        IOptions<ArrOptions> options,
        ILogger<ImageService> logger)
    {
        _dbContext = dbContext;
        _clients = clients;
        _artwork = artwork;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ImageData?> GetAsync(Guid itemId, ImageKind kind, ImageSize size, CancellationToken cancellationToken)
    {
        var candidates = await LoadSourcesAsync(itemId, kind, cancellationToken);
        if (candidates.Count == 0)
        {
            return null;
        }

        // The tag names the sources, so a new poster upstream is a new entry and the old one ages out of the cache
        var key = $"{itemId:N}|{kind}|{size}|{Tag(candidates, kind, size)}";
        if (await _cache.GetAsync(key, cancellationToken) is { } cached)
        {
            return cached;
        }

        var image = await FetchAsync(itemId, kind, size, candidates, cancellationToken);
        await _cache.StoreAsync(key, image, cancellationToken);
        return image;
    }

    /// <inheritdoc />
    public async Task<string?> GetTagAsync(Guid itemId, ImageKind kind, ImageSize size, CancellationToken cancellationToken)
    {
        var candidates = await LoadSourcesAsync(itemId, kind, cancellationToken);
        return candidates.Count == 0 ? null : Tag(candidates, kind, size);
    }

    private async Task<List<MediaImage>> LoadSourcesAsync(Guid itemId, ImageKind kind, CancellationToken cancellationToken) =>
        await _dbContext.MediaImages.AsNoTracking()
            .Where(image => image.MediaItemId == itemId && image.Kind == kind)
            .ToListAsync(cancellationToken);

    private async Task<ImageData> FetchAsync(
        Guid itemId,
        ImageKind kind,
        ImageSize size,
        List<MediaImage> candidates,
        CancellationToken cancellationToken)
    {
        // Every instance that holds the item has its own copy. Try them in configuration order and
        // only fail when none of them can be fetched.
        var instances = _options.Value;
        UpstreamException? lastFailure = null;
        foreach (var candidate in candidates.OrderBy(image => instances.OrderOf(image.InstanceKey)))
        {
            var instance = instances.Instances.FirstOrDefault(instance => instance.Key == candidate.InstanceKey);
            if (instance is not null && candidate is { ArrId: > 0, ArrFileName: not null })
            {
                try
                {
                    var fetch = MediaCover(instance, candidate.ArrId.Value, cancellationToken);
                    var sized = Artwork.SizedArrFileName(candidate.ArrFileName, kind, size);
                    return await SizedOrOriginalAsync(fetch, candidate.ArrFileName, sized);
                }
                catch (UpstreamException exception)
                {
                    _logger.LogInformation("{Upstream} could not serve the {Kind} of item {ItemId}: {Reason}",
                        exception.Upstream, kind, itemId, exception.Message);
                    lastFailure = exception;
                }
            }

            if (candidate.RemoteUrl is not null)
            {
                try
                {
                    var remoteUrl = candidate.RemoteUrl;
                    var sized = Artwork.SizedRemoteUrl(remoteUrl, kind, size);
                    return await SizedOrOriginalAsync(url => _artwork.GetAsync(url, cancellationToken), remoteUrl, sized);
                }
                catch (UpstreamException exception)
                {
                    lastFailure = exception;
                }
            }
        }

        throw lastFailure ?? new UpstreamException("Artwork", UpstreamFailure.NotFound,
            $"The {kind} of this item has no source the server can fetch.");
    }

    private string Tag(List<MediaImage> candidates, ImageKind kind, ImageSize size)
    {
        // Where the picture comes from says which picture it is: a new poster arrives under a new url. Every source
        // counts, in the order they are tried, as any of them may be the one that answers.
        var instances = _options.Value;
        var sources = candidates
            .OrderBy(image => instances.OrderOf(image.InstanceKey))
            .ThenBy(image => image.InstanceKey, StringComparer.Ordinal)
            .Select(image => $"{image.InstanceKey}|{image.ArrId}|{image.ArrFileName}|{image.RemoteUrl}");
        var text = string.Join('\n', sources.Prepend($"{kind}|{size}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 16);
    }

    private Func<string, Task<ImageData>> MediaCover(ArrInstanceOptions instance, int arrId, CancellationToken cancellationToken)
    {
        if (instance.Kind == ArrKind.Radarr)
        {
            var radarr = _clients.CreateRadarr(instance);
            return file => radarr.GetMediaCoverAsync(arrId, file, cancellationToken);
        }

        var sonarr = _clients.CreateSonarr(instance);
        return file => sonarr.GetMediaCoverAsync(arrId, file, cancellationToken);
    }

    // Radarr and Sonarr only keep resized copies of what they resized themselves, so a missing one falls back to the original
    private static async Task<ImageData> SizedOrOriginalAsync(Func<string, Task<ImageData>> fetch, string original, string? sized)
    {
        if (sized is not null)
        {
            try
            {
                return await fetch(sized);
            }
            catch (UpstreamException exception) when (exception.Failure == UpstreamFailure.NotFound)
            {
            }
        }

        return await fetch(original);
    }
}
