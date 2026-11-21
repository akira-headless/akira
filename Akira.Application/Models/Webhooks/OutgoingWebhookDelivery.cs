using System.Text.Json;

namespace Akira.Server.Models.Webhooks;

/// <summary>
/// Represents the body of every webhook delivery. UserId is set on events that belong to one user, such as playback and
/// watch state.
/// </summary>
public sealed record OutgoingWebhookDelivery(long Sequence, string Type, int Version, JsonElement Payload, DateTimeOffset CreatedAt, Guid? UserId);
