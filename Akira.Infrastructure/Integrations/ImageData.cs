namespace Akira.Infrastructure.Integrations;

/// <summary>
/// Represents an image fetched from an upstream, with the content type the server decided for it.
/// </summary>
public sealed record ImageData(byte[] Content, string ContentType);
