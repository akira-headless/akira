namespace Akira.Server.Models.Events;

/// <summary>
/// Represents the answer to a received Radarr or Sonarr webhook. Queued is false when it needed no refresh or the queue
/// was full.
/// </summary>
public sealed record WebhookReceipt(string EventType, bool Queued);
