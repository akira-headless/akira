namespace Akira.Server.Models.Actions;

/// <summary>
/// Represents what removing does by default per type, and whether users other than admins may remove.
/// </summary>
public sealed record ServerRemovalSettings(EffectiveRemoval Defaults, bool UsersMayRemove);
