namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents whether the first admin still has to be created.
/// </summary>
public sealed record SetupStatus(bool SetupRequired);
