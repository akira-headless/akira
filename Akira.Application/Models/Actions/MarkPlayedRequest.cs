namespace Akira.Server.Models.Actions;

/// <summary>
/// Represents the body of a request to mark an item played or unplayed. Played is required.
/// </summary>
public sealed record MarkPlayedRequest(bool? Played);
