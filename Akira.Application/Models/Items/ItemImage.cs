using Akira.Core.Enum;

namespace Akira.Server.Models.Items;

/// <summary>
/// Represents an image of an item. BlurHash is what a client draws while the image loads, null until
/// the server has made it.
/// </summary>
public sealed record ItemImage(ImageKind Kind, string Url, string? BlurHash);
