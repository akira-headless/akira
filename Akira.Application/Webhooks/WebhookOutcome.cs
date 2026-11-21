namespace Akira.Server.Webhooks;

/// <summary>
/// Represents what came of a received Radarr or Sonarr webhook. Queued is false when it needed no refresh or the queue
/// was full.
/// </summary>
public sealed record WebhookOutcome(WebhookRejection Rejection, string? EventType, bool Queued, string? Problem = null)
{
    public static WebhookOutcome Rejected(WebhookRejection rejection, string problem) => new(rejection, null, false, problem);
}
