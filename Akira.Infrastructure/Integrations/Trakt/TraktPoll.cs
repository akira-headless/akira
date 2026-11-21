namespace Akira.Infrastructure.Integrations.Trakt;

/// <summary>
/// Represents one poll of a Trakt device login. Token is only set once the user approved it.
/// </summary>
public sealed record TraktPoll(TraktPollStatus Status, TraktToken? Token = null);
