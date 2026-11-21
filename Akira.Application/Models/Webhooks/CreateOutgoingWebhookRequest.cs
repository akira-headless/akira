namespace Akira.Server.Models.Webhooks;

/// <summary>
/// Represents the body of a request to create a webhook. EventTypes empty or left out is every event.
/// </summary>
public sealed record CreateOutgoingWebhookRequest(string Name, string Url, IReadOnlyList<string>? EventTypes);
