using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Core.Auth;
using Akira.Core.Entities;
using Akira.Infrastructure.Data;
using Akira.Server.Models.Webhooks;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Akira.Server.Services.Webhooks;

public sealed class OutgoingWebhookDispatcher : IOutgoingWebhookDispatcher
{
    public const string HttpClientName = "outgoing-webhooks";
    public const string ProtectorPurpose = "outgoing-webhook-secret";
    private const int BatchSize = 100;
    private const int MaxErrorLength = 500;
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxRetry = TimeSpan.FromHours(1);

    private readonly AkiraDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly IOptions<JsonOptions> _jsonOptions;
    private readonly TimeProvider _time;
    private readonly ILogger<OutgoingWebhookDispatcher> _logger;

    public OutgoingWebhookDispatcher(
        AkiraDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IDataProtectionProvider dataProtection,
        IOptions<JsonOptions> jsonOptions,
        TimeProvider time,
        ILogger<OutgoingWebhookDispatcher> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _dataProtection = dataProtection;
        _jsonOptions = jsonOptions;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListEnabledAsync(CancellationToken cancellationToken) =>
        await _dbContext.OutgoingWebhooks.AsNoTracking()
            .Where(webhook => webhook.Enabled)
            .Select(webhook => webhook.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<OutgoingWebhookRound> DeliverAsync(Guid webhookId, CancellationToken cancellationToken)
    {
        var webhook = await _dbContext.OutgoingWebhooks.SingleOrDefaultAsync(w => w.Id == webhookId, cancellationToken);
        if (webhook is not { Enabled: true })
        {
            return OutgoingWebhookRound.Stop;
        }

        if (webhook.NextAttemptAt > _time.GetUtcNow())
        {
            return OutgoingWebhookRound.Wait;
        }

        var oldest = await _dbContext.Events.MinAsync(e => (long?)e.Sequence, cancellationToken);
        var more = await DeliverBatchAsync(webhook, oldest, cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Deleted while its events went out
            return OutgoingWebhookRound.Stop;
        }

        return more ? OutgoingWebhookRound.More : OutgoingWebhookRound.Wait;
    }

    /// <inheritdoc />
    public async Task<OutgoingWebhookTestResult> SendAsync(OutgoingWebhook webhook, OutgoingWebhookDelivery delivery, CancellationToken cancellationToken)
    {
        string secret;
        try
        {
            secret = _dataProtection.CreateProtector(ProtectorPurpose).Unprotect(webhook.ProtectedSecret);
        }
        catch (CryptographicException)
        {
            return new OutgoingWebhookTestResult(false, null, "The signing secret cannot be read, the server's key ring changed. Rotate the secret.");
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(delivery, _jsonOptions.Value.SerializerOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.UserAgent.ParseAdd($"Akira/{Core.ServerVersion.Number}");
        request.Headers.Add("X-Akira-Event", delivery.Type);
        request.Headers.Add("X-Akira-Delivery", delivery.Sequence.ToString(CultureInfo.InvariantCulture));
        var signature = WebhookSignature.Create(secret, _time.GetUtcNow(), body);
        request.Headers.Add(WebhookSignature.Header, signature);

        try
        {
            using var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            // Only the status counts, so whatever the receiver writes back is not read
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var statusCode = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                return new OutgoingWebhookTestResult(false, statusCode, $"The receiver answered {statusCode} {response.ReasonPhrase}.");
            }

            return new OutgoingWebhookTestResult(true, statusCode, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new OutgoingWebhookTestResult(false, null, "The receiver did not answer within 10 seconds.");
        }
        catch (HttpRequestException exception)
        {
            var message = exception.Message;
            if (message.Length > MaxErrorLength)
            {
                message = message[..MaxErrorLength];
            }

            return new OutgoingWebhookTestResult(false, null, "The receiver could not be reached: " + message);
        }
    }

    private async Task<bool> DeliverBatchAsync(OutgoingWebhook webhook, long? oldest, CancellationToken cancellationToken)
    {
        // Events the retention removed before this webhook got them are gone; it continues after them
        if (oldest is { } first && webhook.LastSequence < first - 1)
        {
            // The next delivery that works clears LastError, so the log is where this gap stays visible
            webhook.LastError = $"Events {webhook.LastSequence + 1} to {first - 1} were removed before they could be delivered.";
            _logger.LogWarning("Webhook {Name} missed events {From} to {To}: they were removed before they could be delivered",
                webhook.Name, webhook.LastSequence + 1, first - 1);
            webhook.LastSequence = first - 1;
        }

        var types = OutgoingWebhookService.ParseTypes(webhook.EventTypes);
        var events = await _dbContext.Events.AsNoTracking()
            .Where(e => e.Sequence > webhook.LastSequence)
            .OrderBy(e => e.Sequence)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var row in events)
        {
            if (types.Count > 0 && !types.Contains(row.Type))
            {
                webhook.LastSequence = row.Sequence;
                continue;
            }

            using var payload = JsonDocument.Parse(row.Payload);
            var payloadElement = payload.RootElement.Clone();
            var delivery = new OutgoingWebhookDelivery(row.Sequence, row.Type, row.Version, payloadElement, row.CreatedAt, row.UserId);
            var result = await SendAsync(webhook, delivery, cancellationToken);
            webhook.LastStatusCode = result.StatusCode;
            if (!result.Succeeded)
            {
                // Later events wait behind this one, so a receiver always gets them in order
                webhook.ConsecutiveFailures++;
                webhook.NextAttemptAt = _time.GetUtcNow() + Backoff(webhook.ConsecutiveFailures);
                webhook.LastError = result.Error;
                _logger.LogWarning("Webhook {Name} failed on event {Sequence} ({Failures} in a row): {Error}", webhook.Name, row.Sequence,
                    webhook.ConsecutiveFailures, result.Error);
                return false;
            }

            webhook.LastSequence = row.Sequence;
            webhook.LastDeliveredAt = _time.GetUtcNow();
            webhook.ConsecutiveFailures = 0;
            webhook.NextAttemptAt = null;
            webhook.LastError = null;
        }

        return events.Count == BatchSize;
    }

    private static TimeSpan Backoff(int failures)
    {
        var doublings = Math.Min(failures - 1, 16);
        var seconds = FirstRetry.TotalSeconds * Math.Pow(2, doublings);
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxRetry.TotalSeconds));
    }
}
