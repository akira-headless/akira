namespace Akira.Server.Interfaces.Services;

/// <summary>
/// Represents one batch of blur hashes. Taken images were looked at and Left of them could not be fetched and wait for
/// the next round. Stopped says their source seems away, and RetryAfter how long it asked to be left alone.
/// </summary>
public sealed record ImageHashBatch(int Taken, int Left, bool Stopped, TimeSpan? RetryAfter);
