using Akira.Contracts.Accounts;

namespace Akira.Contracts.Library;

/// <summary>
/// Defines read access to the library for plugins, as the caller sees it.
/// </summary>
public interface ILibraryAccess
{
    /// <summary>
    /// Lists one page of the movies or series that have something to play.
    /// </summary>
    Task<ItemPage> ListAsync(Caller caller, ItemQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves one item, or null when it does not exist or has nothing to play.
    /// </summary>
    Task<LibraryItem?> GetAsync(Caller caller, Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a movie or series by an id from IMDb, TMDB or TVDB, as Radarr and Sonarr reported it.
    /// </summary>
    Task<LibraryItem?> FindAsync(Caller caller, ExternalIdKind idKind, string id, ItemKind kind, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the seasons of a series or the episodes of a season, in order.
    /// </summary>
    Task<IReadOnlyList<LibraryItem>> ChildrenAsync(Caller caller, Guid parentId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the files of a movie or episode, with their tracks.
    /// </summary>
    Task<IReadOnlyList<MediaVersion>> VersionsAsync(Caller caller, Guid itemId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the files of several movies or episodes at once, by item, for a listing that shows them all. An item
    /// without files is left out.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<MediaVersion>>> VersionsAsync(Caller caller, IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the movies and episodes the caller started and did not finish, most recent first.
    /// </summary>
    Task<IReadOnlyList<LibraryItem>> ContinueWatchingAsync(Caller caller, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the next episode of every series the caller is watching.
    /// </summary>
    Task<IReadOnlyList<LibraryItem>> NextUpAsync(Caller caller, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Reads an image of an item, or null when it has none. It takes no caller, as artwork is not personal and clients
    /// load it without credentials.
    /// </summary>
    Task<ArtworkData?> ArtworkAsync(Guid itemId, ArtworkKind kind, ArtworkSize size, CancellationToken cancellationToken);
}
