using System.Linq.Expressions;
using Akira.Core.Entities;
using Akira.Core.Enum;

namespace Akira.Infrastructure.Sync;

/// <summary>
/// Represents a library item that changed. SeriesId is the series it belongs to, its own id for a series, and null
/// for a movie.
/// </summary>
public sealed record ItemChange(Guid ItemId, MediaItemType Type, string IdentityKey, Guid? ParentId, Guid? SeriesId)
{
    // How a query reads one from an item, so the series of an item is worked out in one place
    public static readonly Expression<Func<MediaItem, ItemChange>> FromItem = item => new ItemChange(
        item.Id,
        item.Type,
        item.IdentityKey,
        item.ParentId,
        item.Type == MediaItemType.Series ? item.Id
        : item.Type == MediaItemType.Season ? item.ParentId
        : item.Type == MediaItemType.Episode ? item.Parent!.ParentId
        : null);
}
