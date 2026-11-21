namespace Akira.Core.Entities;

// A URL other tools gave to hear about events. Deliveries follow the event sequence: LastSequence is the
// last event this webhook has handled, delivered or skipped because of its event types.
public class OutgoingWebhook : BaseEntity
{
    public required string Name { get; set; }
    public required string Url { get; set; }

    // Comma separated event types, empty for every event
    public string EventTypes { get; set; } = "";
    public bool Enabled { get; set; } = true;

    // Protected with ASP.NET Data Protection; the server needs the secret itself to sign, so it cannot keep only a hash
    public required string ProtectedSecret { get; set; }

    public long LastSequence { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? LastDeliveredAt { get; set; }
    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }
}
