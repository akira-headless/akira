namespace Akira.Server.Models.Items;

/// <summary>
/// Represents the answer to a request for a library sync. Queued is false when a sync was already waiting to start.
/// </summary>
public sealed record LibrarySyncReceipt(bool Queued);
