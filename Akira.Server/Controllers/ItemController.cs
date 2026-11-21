using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Authentication;
using Akira.Core.Enum;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Items;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + ItemRoutes.Items)]
[Tags("Library")]
public sealed class ItemController : ApiController
{
    private readonly ILibraryService _library;
    private readonly IImageDeliveryService _images;

    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;
    public const int MaxSearchLength = 200;

    public ItemController(
        ILibraryService library,
        IImageDeliveryService images)
    {
        _library = library;
        _images = images;
    }

    [HttpGet]
    [EndpointName("ListItems")]
    [EndpointSummary("Lists movies and series, or all items of one type.")]
    public async Task<Results<Ok<PagedResponse<ItemSummary>>, ValidationProblem>> ListItems(
        CancellationToken cancellationToken,
        [FromQuery] ApiEnum<MediaItemType>? type = null,
        string? search = null,
        [FromQuery] ApiEnum<ItemSort>? sort = null,
        [FromQuery] ApiEnum<SortOrder>? order = null,
        bool includeUnavailable = false,
        int offset = 0,
        int limit = DefaultLimit,
        bool unwatched = false)
    {
        var errors = ValidatePage(offset, limit);
        if (search?.Length > MaxSearchLength)
        {
            errors["search"] = [$"search is at most {MaxSearchLength} characters."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Titles read naturally A to Z, dates and years newest first
        var effectiveSort = sort?.Value ?? ItemSort.Title;
        var defaultOrder = effectiveSort == ItemSort.Title ? SortOrder.Asc : SortOrder.Desc;
        var effectiveOrder = order?.Value ?? defaultOrder;
        var query = new ItemListQuery(type?.Value, search, effectiveSort, effectiveOrder, includeUnavailable, offset, limit, unwatched);
        var items = await _library.ListAsync(query, User.GetUserId()!.Value, cancellationToken);
        return TypedResults.Ok(items);
    }

    [HttpGet(ItemRoutes.Letters)]
    [EndpointName("ListItemLetters")]
    [EndpointSummary("Lists the first letters of the titles, A to Z, with where each starts in the A to Z list, for jumping to one.")]
    public async Task<Ok<ItemLettersResponse>> ListItemLetters(
        CancellationToken cancellationToken,
        [FromQuery] ApiEnum<MediaItemType>? type = null,
        bool includeUnavailable = false,
        bool unwatched = false)
    {
        var query = new ItemListQuery(type?.Value, null, ItemSort.Title, SortOrder.Asc, includeUnavailable, 0, MaxLimit, unwatched);
        var letters = await _library.LettersAsync(query, User.GetUserId()!.Value, cancellationToken);
        return TypedResults.Ok(letters);
    }

    [HttpGet(ItemRoutes.Item)]
    [EndpointName("GetItem")]
    [EndpointSummary("Retrieves a movie, series, season or episode.")]
    public async Task<Results<Ok<ItemDetail>, ProblemHttpResult>> GetItem(Guid id, CancellationToken cancellationToken)
    {
        var item = await _library.GetAsync(id, User.GetUserId()!.Value, cancellationToken);
        return item is null ? ItemNotFound(id) : TypedResults.Ok(item);
    }

    [HttpGet(ItemRoutes.Children)]
    [EndpointName("ListItemChildren")]
    [EndpointSummary("Lists the seasons of a series or the episodes of a season.")]
    public async Task<Results<Ok<PagedResponse<ItemSummary>>, ValidationProblem, ProblemHttpResult>> ListItemChildren(
        Guid id,
        CancellationToken cancellationToken,
        bool includeUnavailable = false,
        int offset = 0,
        int limit = DefaultLimit)
    {
        var errors = ValidatePage(offset, limit);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var page = new PageQuery(includeUnavailable, offset, limit);
        var children = await _library.ListChildrenAsync(id, page, User.GetUserId()!.Value, cancellationToken);
        return children is null ? ItemNotFound(id) : TypedResults.Ok(children);
    }

    [HttpGet(ItemRoutes.Image)]
    [EndpointName("GetItemImage")]
    [EndpointSummary("Retrieves an image of an item, through Radarr or Sonarr.")]
    [EndpointDescription("""
        size medium or small asks for the smaller copy Radarr, Sonarr or TMDB keep (a poster 500 or 250 pixels high, a backdrop 360
        or 180), and gives the original where there is none. Leave it out for the original. The answer carries an ETag, and a
        request with it in If-None-Match gets a 304 while the picture is the same.
        """)]
    [ProducesResponseType(typeof(void), StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp", "image/gif")]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status504GatewayTimeout, "application/problem+json")]
    public async Task<Results<FileContentHttpResult, StatusCodeHttpResult, ProblemHttpResult>> GetItemImage(
        Guid id,
        [FromRoute] ApiEnum<ImageKind> kind,
        CancellationToken cancellationToken,
        [FromQuery] ApiEnum<ImageSize>? size = null)
    {
        var delivery = await _images.DeliverAsync(id, kind.Value, size?.Value ?? ImageSize.Original, MediaResults.HeldTags(Request), cancellationToken);
        return MediaResults.Image(delivery, id, kind.Value, HttpContext);
    }

    private static Dictionary<string, string[]> ValidatePage(int offset, int limit)
    {
        var errors = new Dictionary<string, string[]>();
        if (offset < 0)
        {
            errors["offset"] = ["offset must be 0 or more."];
        }

        if (limit is < 1 or > MaxLimit)
        {
            errors["limit"] = [$"limit must be between 1 and {MaxLimit}."];
        }

        return errors;
    }

    private static ProblemHttpResult ItemNotFound(Guid id) =>
        TypedResults.Problem(title: "Item not found", detail: $"Item {id} does not exist.", statusCode: StatusCodes.Status404NotFound);
}
