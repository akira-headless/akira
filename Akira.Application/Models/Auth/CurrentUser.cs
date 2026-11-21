namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents the signed in user and the device the request came from. Credential is device, profile or apiKey, and
/// Device is null for an API key.
/// </summary>
public sealed record CurrentUser(UserSummary User, DeviceSummary? Device, string Credential);
