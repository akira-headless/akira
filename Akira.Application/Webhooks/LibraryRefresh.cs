namespace Akira.Server.Webhooks;

/// <summary>
/// Represents a queued refresh of one movie or series. ArrId is the Radarr movie id or the Sonarr series id.
/// </summary>
public sealed record LibraryRefresh(string InstanceKey, int ArrId);
