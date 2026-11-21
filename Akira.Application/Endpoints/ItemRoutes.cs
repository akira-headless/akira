using System.Text.Json;
using Akira.Core.Enum;

namespace Akira.Server.Endpoints;

/// <summary>
/// Defines the item routes, so the urls handed to clients and the mapped routes come from one place.
/// </summary>
public static class ItemRoutes
{
    public const string ApiPrefix = "/api/v1";
    public const string Items = "/items";
    public const string Files = "/files";
    public const string Item = "{id:guid}";
    public const string Letters = "letters";
    public const string Children = "{id:guid}/children";
    public const string Image = "{id:guid}/images/{kind}";

    public static string ImageUrl(Guid itemId, ImageKind kind) =>
        $"{ApiPrefix}{Items}/{itemId}/images/{JsonNamingPolicy.CamelCase.ConvertName(kind.ToString())}";
}
