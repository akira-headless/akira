namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents a profile a device can use. HasPin says whether picking it needs a PIN.
/// </summary>
public sealed record ProfileSummary(Guid Id, string Name, bool HasPin);
