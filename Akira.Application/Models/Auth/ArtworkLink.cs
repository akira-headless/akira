namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents a url for any item's artwork. UrlTemplate has {itemId} and {kind} (an image kind) to fill in, and needs
/// no credential.
/// </summary>
public sealed record ArtworkLink(string UrlTemplate);
