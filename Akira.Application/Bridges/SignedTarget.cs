namespace Akira.Server.Bridges;

/// <summary>
/// Represents what a signed url points at. Detail is the stream id of a subtitle, the kind and size of artwork, or the
/// user an HLS session or progressive stream is for.
/// </summary>
public sealed record SignedTarget(SignedKind Kind, Guid Id, string? Detail, Guid DeviceId)
{
    // The credential the url was made for, which it ends with: a profile session ends long before its device does
    public Guid? TokenId { get; init; }

    // Set by the resolver for a bridge without sign in (DLNA), whose urls work where the bridge does: the local network
    public bool LocalOnly { get; init; }
}
