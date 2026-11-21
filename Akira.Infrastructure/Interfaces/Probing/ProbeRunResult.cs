namespace Akira.Infrastructure.Interfaces.Probing;

/// <summary>
/// Represents what a probe run did. ProbedItems are the items whose files got probed tracks, which is what changes for
/// a client.
/// </summary>
public sealed record ProbeRunResult(int Probed, int Missing, int Failed, IReadOnlyList<Guid> ProbedItems);
