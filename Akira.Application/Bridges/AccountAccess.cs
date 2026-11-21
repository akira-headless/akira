using System.Diagnostics.CodeAnalysis;
using System.Net;
using Akira.Contracts.Accounts;
using Microsoft.EntityFrameworkCore;
using Akira.Core.Auth;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Auth;
using Akira.Server.Services.Auth;

namespace Akira.Server.Bridges;

public sealed class AccountAccess : IAccountAccess
{
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    private readonly AkiraDbContext _dbContext;
    private readonly IPairingService _pairings;
    private readonly TimeProvider _time;

    public AccountAccess(
        AkiraDbContext dbContext,
        IPairingService pairings,
        TimeProvider time)
    {
        _dbContext = dbContext;
        _pairings = pairings;
        _time = time;
    }

    /// <inheritdoc />
    public Task<Caller?> AuthenticateLinkAsync(string bridgeKey, string token, CancellationToken cancellationToken) =>
        token.StartsWith(Secrets.LinkTokenPrefix, StringComparison.Ordinal)
            ? FindAsync(token, kind => kind == DeviceKind.Link, bridgeKey, cancellationToken)
            : Task.FromResult<Caller?>(null);

    /// <inheritdoc />
    public Task<Caller?> AuthenticateDeviceAsync(string token, CancellationToken cancellationToken) =>
        token.StartsWith(Secrets.DeviceTokenPrefix, StringComparison.Ordinal)
            ? FindAsync(token, kind => kind != DeviceKind.Link, null, cancellationToken)
            : Task.FromResult<Caller?>(null);

    /// <inheritdoc />
    public async Task<PairingStart?> StartPairingAsync(string deviceName, string client, string? clientVersion, string? platform,
        IPAddress? from, CancellationToken cancellationToken)
    {
        // What an app says about itself is cut to what pairing takes, rather than refused
        var request = new StartPairingRequest(Cut(deviceName), Cut(client), Cut(clientVersion), Cut(platform));
        var result = await _pairings.StartAsync(request, from, cancellationToken);
        if (!result.Succeeded)
        {
            return null;
        }

        var started = result.Value!;
        return new PairingStart(started.PairingId, started.Code, started.PollSecret, started.ExpiresAt);
    }

    /// <inheritdoc />
    public async Task<PairingPoll> PollPairingAsync(Guid pairingId, string pollSecret, CancellationToken cancellationToken)
    {
        var result = await _pairings.PollAsync(pairingId, pollSecret, cancellationToken);
        if (!result.Succeeded)
        {
            return new PairingPoll(PairingState.Gone, null, null);
        }

        if (result.Value!.Completed is not { } completed)
        {
            return new PairingPoll(PairingState.Waiting, null, null);
        }

        // A shared device without a profile acts for nobody, which a bridge cannot use
        var caller = await AuthenticateDeviceAsync(completed.Token, cancellationToken);
        if (caller is null)
        {
            return new PairingPoll(PairingState.Gone, null, null);
        }

        return new PairingPoll(PairingState.Approved, completed.Token, caller);
    }

    /// <inheritdoc />
    public async Task<Caller?> FixedUserAsync(string bridgeKey, string userName, CancellationToken cancellationToken)
    {
        // By the normalized name, so the setting matches the way people type it on SQLite and MariaDB alike
        var normalized = UserNames.Normalize(userName);
        var user = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.NormalizedName == normalized)
            .Select(u => new { u.Id, u.Name, u.Role })
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return null;
        }

        var device = await _dbContext.Devices.AsNoTracking()
            .Where(d => d.Kind == DeviceKind.Link && d.Client == bridgeKey && d.UserId == user.Id && !d.Tokens.Any())
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (device is null)
        {
            var link = new Device
            {
                Name = bridgeKey.ToUpperInvariant() + " as " + user.Name,
                Client = bridgeKey,
                Kind = DeviceKind.Link,
                UserId = user.Id
            };
            _dbContext.Devices.Add(link);
            await _dbContext.SaveChangesAsync(cancellationToken);
            device = link.Id;
        }

        return new Caller(user.Id, user.Name, device.Value, user.Role == UserRole.Admin);
    }

    private async Task<Caller?> FindAsync(string token, Func<DeviceKind, bool> kind, string? bridgeKey, CancellationToken cancellationToken)
    {
        var hash = Secrets.Hash(token);
        var now = _time.GetUtcNow();
        var found = await _dbContext.DeviceTokens.AsNoTracking()
            .Where(t => t.TokenHash == hash && t.UserId != null && (t.ExpiresAt == null || t.ExpiresAt > now))
            .Select(t => new
            {
                t.Id, t.DeviceId, UserId = t.UserId!.Value, UserName = t.User!.Name, t.User.Role, t.LastUsedAt, t.Device.Kind, t.Device.Client
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (found is null || !kind(found.Kind) || (bridgeKey is not null && found.Client != bridgeKey))
        {
            return null;
        }

        // Devices shows when it was last used, the same as for the API
        if (found.LastUsedAt is null || now - found.LastUsedAt >= TouchInterval)
        {
            await _dbContext.DeviceTokens.Where(t => t.Id == found.Id)
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.LastUsedAt, now), cancellationToken);
            await _dbContext.Devices.Where(d => d.Id == found.DeviceId)
                .ExecuteUpdateAsync(d => d.SetProperty(x => x.LastSeenAt, now), cancellationToken);
        }

        return new Caller(found.UserId, found.UserName, found.DeviceId, found.Role == UserRole.Admin) { TokenId = found.Id };
    }

    [return: NotNullIfNotNull(nameof(value))]
    private static string? Cut(string? value)
    {
        var trimmed = value?.Trim();
        if (trimmed is { Length: > AuthValidation.MaxNameLength })
        {
            return trimmed[..AuthValidation.MaxNameLength];
        }

        return trimmed;
    }
}
