using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Akira.Core.Auth;
using Akira.Core.Entities;
using Akira.Server.Events;
using Akira.Infrastructure.Data;
using Akira.Server.Models.Webhooks;
using Akira.Server.Services.Auth;

namespace Akira.Server.Services.Webhooks;

public sealed class OutgoingWebhookService : IOutgoingWebhookService
{
    public const string TestEvent = "webhook.test";
    private const int MaxUrlLength = 2000;

    private readonly AkiraDbContext _dbContext;
    private readonly IOutgoingWebhookDispatcher _dispatcher;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly EventSignal _signal;
    private readonly TimeProvider _time;
    private readonly ILogger<OutgoingWebhookService> _logger;

    public OutgoingWebhookService(
        AkiraDbContext dbContext,
        IOutgoingWebhookDispatcher dispatcher,
        IDataProtectionProvider dataProtection,
        EventSignal signal,
        TimeProvider time,
        ILogger<OutgoingWebhookService> logger)
    {
        _dbContext = dbContext;
        _dispatcher = dispatcher;
        _dataProtection = dataProtection;
        _signal = signal;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutgoingWebhookSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var webhooks = await _dbContext.OutgoingWebhooks.AsNoTracking().OrderBy(webhook => webhook.Name).ToListAsync(cancellationToken);
        return webhooks.Select(ToSummary).ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CreatedOutgoingWebhook>> CreateAsync(CreateOutgoingWebhookRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request.Name, request.Url, request.EventTypes) is { } error)
        {
            return ServiceResult<CreatedOutgoingWebhook>.Fail(ServiceError.Invalid, error);
        }

        var secret = Secrets.NewWebhookSecret();
        // From now on: a new receiver does not get the history
        var lastSequence = await _dbContext.Events.MaxAsync(e => (long?)e.Sequence, cancellationToken);
        var webhook = new OutgoingWebhook
        {
            Name = request.Name.Trim(),
            Url = request.Url.Trim(),
            EventTypes = JoinTypes(request.EventTypes),
            ProtectedSecret = Protect(secret),
            LastSequence = lastSequence ?? 0
        };
        _dbContext.OutgoingWebhooks.Add(webhook);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Outgoing webhook {Name} added", webhook.Name);
        var created = new CreatedOutgoingWebhook(ToSummary(webhook), secret);
        return ServiceResult<CreatedOutgoingWebhook>.Ok(created);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<OutgoingWebhookSummary>> UpdateAsync(Guid id, UpdateOutgoingWebhookRequest request, CancellationToken cancellationToken)
    {
        var webhook = await _dbContext.OutgoingWebhooks.SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (webhook is null)
        {
            return ServiceResult<OutgoingWebhookSummary>.Fail(ServiceError.NotFound, $"Webhook {id} does not exist.");
        }

        var name = request.Name ?? webhook.Name;
        var url = request.Url ?? webhook.Url;
        if (Validate(name, url, request.EventTypes) is { } error)
        {
            return ServiceResult<OutgoingWebhookSummary>.Fail(ServiceError.Invalid, error);
        }

        webhook.Name = name.Trim();
        webhook.Url = url.Trim();
        if (request.EventTypes is not null)
        {
            webhook.EventTypes = JoinTypes(request.EventTypes);
        }

        if (request.Enabled is { } enabled)
        {
            webhook.Enabled = enabled;
        }

        // A change is a reason to try again now instead of after the backoff
        webhook.NextAttemptAt = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        _signal.Pulse();
        return ServiceResult<OutgoingWebhookSummary>.Ok(ToSummary(webhook));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var removed = await _dbContext.OutgoingWebhooks.Where(w => w.Id == id).ExecuteDeleteAsync(cancellationToken);
        if (removed == 0)
        {
            return ServiceResult<bool>.Fail(ServiceError.NotFound, $"Webhook {id} does not exist.");
        }

        return ServiceResult<bool>.Ok(true);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CreatedOutgoingWebhook>> RotateSecretAsync(Guid id, CancellationToken cancellationToken)
    {
        var webhook = await _dbContext.OutgoingWebhooks.SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (webhook is null)
        {
            return ServiceResult<CreatedOutgoingWebhook>.Fail(ServiceError.NotFound, $"Webhook {id} does not exist.");
        }

        var secret = Secrets.NewWebhookSecret();
        webhook.ProtectedSecret = Protect(secret);
        webhook.NextAttemptAt = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        var rotated = new CreatedOutgoingWebhook(ToSummary(webhook), secret);
        return ServiceResult<CreatedOutgoingWebhook>.Ok(rotated);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<OutgoingWebhookTestResult>> TestAsync(Guid id, CancellationToken cancellationToken)
    {
        var webhook = await _dbContext.OutgoingWebhooks.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (webhook is null)
        {
            return ServiceResult<OutgoingWebhookTestResult>.Fail(ServiceError.NotFound, $"Webhook {id} does not exist.");
        }

        using var payload = JsonDocument.Parse("""{"message":"A test from Akira. Deliveries look like this one."}""");
        var delivery = new OutgoingWebhookDelivery(0, TestEvent, 1, payload.RootElement.Clone(), _time.GetUtcNow(), null);
        var result = await _dispatcher.SendAsync(webhook, delivery, cancellationToken);
        return ServiceResult<OutgoingWebhookTestResult>.Ok(result);
    }

    public static IReadOnlyList<string> ParseTypes(string types) =>
        types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string JoinTypes(IReadOnlyList<string>? types)
    {
        var trimmed = (types ?? []).Select(t => t.Trim()).Distinct();
        return string.Join(',', trimmed);
    }

    private static string? Validate(string? name, string? url, IReadOnlyList<string>? types)
    {
        if (AuthValidation.Text(name, "name", AuthValidation.MaxNameLength) is { } nameError)
        {
            return nameError;
        }

        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxUrlLength || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return "url must be an absolute http or https url.";
        }

        // The url is listed back to admins; a password in it would be on every screen
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "url cannot hold a user name or password. Check the X-Akira-Signature header on the receiving side instead.";
        }

        var unknown = (types ?? []).Where(type => !EventTypes.Stored.Contains(type.Trim())).ToList();
        if (unknown.Count > 0)
        {
            return $"Unknown event types: {string.Join(", ", unknown)}. Known are {string.Join(", ", EventTypes.Stored)}.";
        }

        return null;
    }

    private string Protect(string secret) => _dataProtection.CreateProtector(OutgoingWebhookDispatcher.ProtectorPurpose).Protect(secret);

    private static OutgoingWebhookSummary ToSummary(OutgoingWebhook webhook) => new(
        webhook.Id, webhook.Name, webhook.Url, ParseTypes(webhook.EventTypes), webhook.Enabled, webhook.LastSequence, webhook.ConsecutiveFailures,
        webhook.NextAttemptAt, webhook.LastDeliveredAt, webhook.LastStatusCode, webhook.LastError, webhook.CreatedAt);
}
