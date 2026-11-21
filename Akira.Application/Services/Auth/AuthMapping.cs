using Akira.Core.Entities;
using Akira.Server.Models.Auth;

namespace Akira.Server.Services.Auth;

internal static class AuthMapping
{
    public static UserSummary ToSummary(this User user) => new(user.Id, user.Name, user.Role, user.PinHash is not null);

    public static DeviceSummary ToSummary(this Device device) => new(
        device.Id, device.Name, device.Client, device.ClientVersion, device.Platform, device.Kind,
        device.UserId, device.User?.Name, device.CreatedAt, device.LastSeenAt);

    public static ApiKeySummary ToSummary(this ApiKey key) => new(
        key.Id, key.Name, key.Prefix, key.UserId, key.User.Name, key.CreatedAt, key.LastUsedAt);
}
