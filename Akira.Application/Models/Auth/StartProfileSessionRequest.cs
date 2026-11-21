namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents the body of a request to pick a profile on a shared device. Pin is needed when the profile has one.
/// </summary>
public sealed record StartProfileSessionRequest(string? Pin);
