namespace Akira.Server.Models.Webhooks;

/// <summary>
/// Represents what came of a test delivery to a webhook, with the receiver's status code when it answered.
/// </summary>
public sealed record OutgoingWebhookTestResult(bool Succeeded, int? StatusCode, string? Error);
