namespace Akira.Server.Models.Trakt;

/// <summary>
/// Represents a Trakt link that waits for the user to enter UserCode at VerificationUrl. Poll every
/// IntervalSeconds until ExpiresAt.
/// </summary>
public sealed record TraktPending(string UserCode, string VerificationUrl, DateTimeOffset ExpiresAt, int IntervalSeconds);
