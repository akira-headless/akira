using Akira.Infrastructure.Integrations;
using Akira.Server.Models.Files;

namespace Akira.Server.Models.Items;

/// <summary>
/// Represents an item's image to answer a request with: the picture with its tag, NotModified when the client holds that
/// tag already, NotFound, or Failed with the upstream failure and its message.
/// </summary>
public sealed record ImageDelivery(DeliveryStatus Status, string? Tag = null, ImageData? Image = null)
{
    public UpstreamFailure? Failure { get; init; }
    public string? Detail { get; init; }
}
