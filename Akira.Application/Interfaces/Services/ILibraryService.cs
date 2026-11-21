using Akira.Server.Models.Items;

namespace Akira.Server.Interfaces.Services;

public interface ILibraryService
{
    /// <summary>
    /// Lists movies and series, or items of one type, as a page with the user's watch state.
    /// </summary>
    Task<PagedResponse<ItemSummary>> ListAsync(ItemListQuery query, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves one item with its versions, or null when it does not exist.
    /// </summary>
    Task<ItemDetail?> GetAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the seasons of a series or the episodes of a season, or null when the parent does not exist.
    /// </summary>
    Task<PagedResponse<ItemSummary>?> ListChildrenAsync(Guid id, PageQuery query, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists what the user started and did not finish, most recent first.
    /// </summary>
    Task<IReadOnlyList<ItemSummary>> ContinueWatchingAsync(Guid userId, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the next episode to watch of every series the user is in, most recently watched series first.
    /// </summary>
    Task<IReadOnlyList<ItemSummary>> NextUpAsync(Guid userId, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the first letters of the titles the query matches, A to Z, each with where it starts in that list and how many it has.
    /// </summary>
    Task<ItemLettersResponse> LettersAsync(ItemListQuery query, Guid userId, CancellationToken cancellationToken);
}
