using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Library;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Integrations.Radarr;

namespace Akira.Infrastructure.Sync;

/// <summary>
/// Applies what one Radarr or Sonarr instance reports to tracked library entities.
/// </summary>
internal static class LibraryItems
{
    // Ids are set on construction, so every new row is added explicitly. One reached only through
    // a navigation of an existing entity would be taken for an existing row and updated instead.

    public static void Link(AkiraDbContext dbContext, MediaItem item, string instanceKey, int? arrId)
    {
        var link = item.Links.FirstOrDefault(link => link.InstanceKey == instanceKey);
        if (link is null)
        {
            link = new ArrLink { MediaItemId = item.Id, InstanceKey = instanceKey };
            item.Links.Add(link);
            dbContext.ArrLinks.Add(link);
        }

        link.ArrId = arrId;
    }

    public static bool IsLinked(MediaItem item, string instanceKey) =>
        item.Links.Any(link => link.InstanceKey == instanceKey);

    /// <summary>
    /// Says whether this instance is the one that describes the item: the first configured one that holds it.
    /// </summary>
    public static bool Describes(MediaItem item, string instanceKey, ArrOptions options)
    {
        // Two instances rarely say the same about one title. Each writing its own words over the other's
        // would change the item at every sync, with an event and a reload in every client.
        var order = options.OrderOf(instanceKey);
        return item.Links.All(link => options.OrderOf(link.InstanceKey) >= order);
    }

    /// <summary>
    /// Writes what Radarr and Sonarr say in the same words. False when the language has no ISO 639-2 code.
    /// </summary>
    public static bool Describe(MediaItem item, ItemDescription description)
    {
        item.Title = description.Title.Trim();

        // Folded as the database context stores it, or an accent would make the row differ at every sync
        item.SortTitle = SearchText.Fold(NullIfEmpty(description.SortTitle) ?? item.Title);
        item.Year = Positive(description.Year);
        item.Overview = NullIfEmpty(description.Overview);
        item.RuntimeMinutes = Positive(description.Runtime);
        item.TmdbId = Positive(description.TmdbId);
        item.ImdbId = Fitting(description.ImdbId, MediaItem.ImdbIdLength);
        item.Genres = Genres(description.Genres);
        item.Certification = Fitting(description.Certification, MediaItem.CertificationLength);

        var language = description.OriginalLanguage?.Name;
        item.OriginalLanguage = ArrLanguages.ToIso6392(language);
        return item.OriginalLanguage is not null || ArrLanguages.IsPlaceholder(language);
    }

    // The earliest instance to add it decides, so a 4K copy added later does not make it "new"
    public static void AddedNoLaterThan(MediaItem item, DateTimeOffset? added)
    {
        if (item.AddedAt is null || added < item.AddedAt)
        {
            item.AddedAt = added;
        }
    }

    /// <summary>
    /// Removes everything one instance contributed to an item. The item itself goes once no instance holds it.
    /// </summary>
    public static void Unlink(AkiraDbContext dbContext, MediaItem item, string instanceKey)
    {
        foreach (var link in item.Links.Where(link => link.InstanceKey == instanceKey).ToList())
        {
            item.Links.Remove(link);
            dbContext.ArrLinks.Remove(link);
        }

        RemoveFile(dbContext, item, instanceKey);
        foreach (var image in item.Images.Where(image => image.InstanceKey == instanceKey).ToList())
        {
            item.Images.Remove(image);
            dbContext.MediaImages.Remove(image);
        }
    }

    /// <summary>
    /// Adds, keeps or replaces the instance's file of an item. False when its path was refused, which removes the file.
    /// </summary>
    public static bool SyncFile(
        AkiraDbContext dbContext,
        MediaItem item,
        string instanceKey,
        int? arrFileId,
        string? arrPath,
        long size,
        DateTimeOffset? addedAt,
        LibraryPaths paths)
    {
        if (arrFileId is not > 0 || string.IsNullOrWhiteSpace(arrPath))
        {
            RemoveFile(dbContext, item, instanceKey);
            return true;
        }

        if (paths.Resolve(arrPath) is not { } path)
        {
            RemoveFile(dbContext, item, instanceKey);
            return false;
        }

        var current = item.Files.FirstOrDefault(file => file.InstanceKey == instanceKey);
        if (current is not null && current.ArrFileId == arrFileId && current.Path == path)
        {
            current.SizeBytes = size;
            current.AddedAt = addedAt;
            return true;
        }

        // An upgrade or rename is a different file. It gets a new id so the stable track ids
        // derived from it change too, while the item and its per user state stay.
        RemoveFile(dbContext, item, instanceKey);
        var replacement = new MediaFile
        {
            MediaItemId = item.Id,
            InstanceKey = instanceKey,
            Path = path,
            SizeBytes = size,
            Container = GetContainer(path),
            ArrFileId = arrFileId.Value,
            AddedAt = addedAt
        };
        item.Files.Add(replacement);
        dbContext.MediaFiles.Add(replacement);
        return true;
    }

    public static void RemoveFile(AkiraDbContext dbContext, MediaItem item, string instanceKey)
    {
        foreach (var file in item.Files.Where(file => file.InstanceKey == instanceKey).ToList())
        {
            item.Files.Remove(file);
            dbContext.MediaFiles.Remove(file);
        }
    }

    public static void SyncImages(
        AkiraDbContext dbContext,
        MediaItem item,
        string instanceKey,
        int? arrId,
        IEnumerable<ArrImage>? images)
    {
        var wanted = new Dictionary<ImageKind, (string? FileName, string? RemoteUrl)>();
        foreach (var image in images ?? [])
        {
            var kind = Artwork.ParseCoverType(image.CoverType);
            var fileName = arrId > 0 ? Fitting(Artwork.ParseArrFileName(image.Url), MediaImage.ArrFileNameLength) : null;
            var remoteUrl = Artwork.ValidateRemoteUrl(image.RemoteUrl) ?? Artwork.ValidateRemoteUrl(image.Url);
            if (kind is null || (fileName is null && remoteUrl is null))
            {
                continue;
            }

            wanted.TryAdd(kind.Value, (fileName, remoteUrl));
        }

        foreach (var existing in item.Images.Where(image => image.InstanceKey == instanceKey).ToList())
        {
            if (!wanted.Remove(existing.Kind, out var update))
            {
                item.Images.Remove(existing);
                dbContext.MediaImages.Remove(existing);
                continue;
            }

            // Another picture gets its own blur; the same file name keeps it, Radarr and Sonarr rarely replace a cover in place
            if (existing.ArrFileName != update.FileName || existing.RemoteUrl != update.RemoteUrl)
            {
                existing.BlurHash = null;
            }

            existing.ArrId = update.FileName is null ? null : arrId;
            existing.ArrFileName = update.FileName;
            existing.RemoteUrl = update.RemoteUrl;
        }

        foreach (var (kind, (fileName, remoteUrl)) in wanted)
        {
            var image = new MediaImage
            {
                MediaItemId = item.Id,
                InstanceKey = instanceKey,
                Kind = kind,
                ArrId = fileName is null ? null : arrId,
                ArrFileName = fileName,
                RemoteUrl = remoteUrl
            };
            item.Images.Add(image);
            dbContext.MediaImages.Add(image);
        }
    }

    public static string? GetContainer(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension) || extension.Length > 21)
        {
            return null;
        }

        return extension[1..].ToLowerInvariant();
    }

    // A failed save says "see the inner exception", which is where the database names the column
    public static string Reason(Exception exception) => exception.GetBaseException().Message;

    public static int? Positive(int? value) => value > 0 ? value : null;

    public static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // A value too long for its column is not what the column is for, and MariaDB refuses the whole save over it
    public static string? Fitting(string? value, int length) =>
        NullIfEmpty(value) is { } text && text.Length <= length ? text : null;

    // A score nobody voted on is no score
    public static double? Score(ArrRating? rating) =>
        rating is { Votes: > 0, Value: > 0 } ? Math.Round(rating.Value, 1) : null;

    public static string? Genres(IReadOnlyList<string>? genres)
    {
        var named = (genres ?? []).Where(genre => !string.IsNullOrWhiteSpace(genre)).Select(genre => genre.Trim());
        var joined = string.Join(", ", named);
        if (joined.Length <= 500)
        {
            return joined.Length == 0 ? null : joined;
        }

        // Cut between two genres, so none comes out as a fragment
        var cut = joined.LastIndexOf(", ", 500, StringComparison.Ordinal);
        return cut > 0 ? joined[..cut] : null;
    }

    // A YouTube video id is 11 letters, digits, dashes and underscores; anything else is not one
    public static string? TrailerId(string? id)
    {
        if (id is not { Length: > 0 and <= 32 })
        {
            return null;
        }

        return id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? id : null;
    }
}
