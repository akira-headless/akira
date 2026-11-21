namespace Akira.Server.Models.Health;

/// <summary>
/// Represents the health of the server. Status follows the database only, since without ffprobe files just stay
/// unprobed.
/// </summary>
public sealed record HealthResponse(HealthState Status, string Version, HealthState Database, HealthState Ffprobe);
