using Akira.Core.Entities;
using Akira.Server.Models.Webhooks;

namespace Akira.Server.Services.Webhooks;

public interface IOutgoingWebhookDispatcher
{
    /// <summary>
    /// Lists the webhooks that are enabled.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListEnabledAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Delivers the next events to one webhook when it is due, in sequence order, and says what it needs next.
    /// </summary>
    Task<OutgoingWebhookRound> DeliverAsync(Guid webhookId, CancellationToken cancellationToken);

    /// <summary>
    /// Sends one delivery to a webhook and reports how it went, without touching its cursor.
    /// </summary>
    Task<OutgoingWebhookTestResult> SendAsync(OutgoingWebhook webhook, OutgoingWebhookDelivery delivery, CancellationToken cancellationToken);
}
