namespace Akira.Contracts.Accounts;

/// <summary>
/// Represents who a bridge request acts for: the user, and the device or link its credential belongs to. TokenId names
/// the credential itself, so what is made for the caller ends with it, and is null for the fixed user of a bridge.
/// </summary>
public sealed record Caller(Guid UserId, string UserName, Guid DeviceId, bool IsAdmin)
{
    public Guid? TokenId { get; init; }
}
