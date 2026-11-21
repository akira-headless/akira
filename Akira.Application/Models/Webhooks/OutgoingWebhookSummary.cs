namespace Akira.Server.Models.Webhooks;

/// <summary>
/// Represents a webhook and how its deliveries go. LastSequence is the last event it handled, and
/// EventTypes empty is every event.
/// </summary>
public sealed record OutgoingWebhookSummary(
    Guid Id,
    string Name,
    string Url,
    IReadOnlyList<string> EventTypes,
    bool Enabled,
    long LastSequence,
    int ConsecutiveFailures,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? LastDeliveredAt,
    int? LastStatusCode,
    string? LastError,
    DateTimeOffset CreatedAt);
