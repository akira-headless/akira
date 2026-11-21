namespace Akira.Server.Services.Webhooks;

/// <summary>
/// Defines what a webhook needs after one delivery round.
/// </summary>
public enum OutgoingWebhookRound
{
    // Caught up, or waiting for its next retry
    Wait,
    More,
    // Deleted or disabled
    Stop
}
