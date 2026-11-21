namespace Akira.Server.Models.Webhooks;

/// <summary>
/// Represents the body of a request to change a webhook. A field left out stays as it is.
/// </summary>
public sealed record UpdateOutgoingWebhookRequest(string? Name, string? Url, IReadOnlyList<string>? EventTypes, bool? Enabled);
