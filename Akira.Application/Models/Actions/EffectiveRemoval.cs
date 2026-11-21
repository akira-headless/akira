using Akira.Core.Enum;

namespace Akira.Server.Models.Actions;

/// <summary>
/// Represents what removing does for each type of item, with every type filled in.
/// </summary>
public sealed record EffectiveRemoval(RemovalBehavior Movie, RemovalBehavior Episode, RemovalBehavior Season, RemovalBehavior Series);
