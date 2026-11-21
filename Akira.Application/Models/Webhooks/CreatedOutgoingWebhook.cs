namespace Akira.Server.Models.Webhooks;

/// <summary>
/// Represents a webhook with its signing secret. The secret is only in this response, and in the one after rotating it.
/// </summary>
public sealed record CreatedOutgoingWebhook(OutgoingWebhookSummary Webhook, string Secret);
