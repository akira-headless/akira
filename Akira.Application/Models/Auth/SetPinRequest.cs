namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents the body of a request to set, change or remove a PIN. A null NewPin removes it, and CurrentPin is needed
/// to change your own existing PIN.
/// </summary>
public sealed record SetPinRequest(string? CurrentPin, string? NewPin);
