namespace Akira.Server.Models.Files;

/// <summary>
/// Represents a file of the library to answer a request with, or why there is none. Detail is the instance whose path
/// mappings to check when the file is not on disk, and the reason when it could not be made.
/// </summary>
public sealed record FileDelivery(DeliveryStatus Status, string? Path = null, string? ContentType = null, string? Detail = null)
{
    public long Length { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public string? EntityTag { get; init; }
}
