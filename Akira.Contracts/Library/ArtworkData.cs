namespace Akira.Contracts.Library;

/// <summary>
/// Represents the bytes of an image and the content type to serve them with.
/// </summary>
public sealed record ArtworkData(byte[] Content, string ContentType);
