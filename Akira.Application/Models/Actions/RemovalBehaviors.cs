using Akira.Core.Enum;

namespace Akira.Server.Models.Actions;

/// <summary>
/// Represents a user's own choice of what removing does per type. A null type follows the server default.
/// </summary>
public sealed record RemovalBehaviors(RemovalBehavior? Movie, RemovalBehavior? Episode, RemovalBehavior? Season, RemovalBehavior? Series);
