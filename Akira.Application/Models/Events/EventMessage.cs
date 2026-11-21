using System.Text.Json;

namespace Akira.Server.Models.Events;

/// <summary>
/// Represents one event as a client receives it. Sequence is 0 for a message that is never stored, such as a remote
/// command.
/// </summary>
public sealed record EventMessage(long Sequence, string Type, int Version, JsonElement Payload, DateTimeOffset CreatedAt);
