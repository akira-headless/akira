namespace Akira.Server.Models.Actions;

/// <summary>
/// Represents a user's removal override, what removing does for them, and whether they may remove at all.
/// </summary>
public sealed record UserRemovalSettings(RemovalBehaviors Override, EffectiveRemoval Effective, bool MayRemove);
