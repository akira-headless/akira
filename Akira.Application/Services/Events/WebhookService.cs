using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Akira.Core.Auth;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Server.Interfaces.Services;
using Akira.Server.Webhooks;

namespace Akira.Server.Services.Events;

public class WebhookService : IWebhookService
{
    // Nothing about a file or an item changes with these, so they refresh nothing
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "Test", "Grab", "Health", "HealthRestored", "ApplicationUpdate", "ManualInteractionRequired"
    };

    private readonly IOptions<ArrOptions> _options;
    private readonly LibraryRefreshQueue _queue;
    private readonly ILogger<WebhookService> _logger;

    public WebhookService(
        IOptions<ArrOptions> options,
        LibraryRefreshQueue queue,
        ILogger<WebhookService> logger)
    {
        _options = options;
        _queue = queue;
        _logger = logger;
    }

    /// <inheritdoc />
    public WebhookOutcome Receive(string instanceKey, string? authorization, JsonElement body)
    {
        var instance = _options.Value.Instances.FirstOrDefault(instance => instance.Key == instanceKey);
        if (instance is null || string.IsNullOrEmpty(instance.WebhookSecret))
        {
            // Said only in the log: an anonymous caller learns nothing about what is configured
            _logger.LogWarning(instance is null
                    ? "Webhook for unknown instance {Instance}"
                    : "Webhook for {Instance}, which has no WebhookSecret configured",
                instanceKey);
            return WebhookOutcome.Rejected(WebhookRejection.UnknownInstance, $"No webhook is set up for instance {instanceKey}.");
        }

        if (ReadPassword(authorization) is not { } secret || !Secrets.FixedTimeEquals(secret, instance.WebhookSecret))
        {
            _logger.LogWarning("{Instance} webhook refused: wrong or missing secret", instance.DisplayName);
            return WebhookOutcome.Rejected(WebhookRejection.Unauthorized,
                "Send the instance's WebhookSecret as the password of HTTP basic authentication, in the Username and Password of the webhook connection.");
        }

        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("eventType", out var eventTypeElement)
            || eventTypeElement.GetString() is not { Length: > 0 } eventType)
        {
            return WebhookOutcome.Rejected(WebhookRejection.Malformed, "The body is not a Radarr or Sonarr webhook, it has no eventType.");
        }

        var subject = instance.Kind == ArrKind.Radarr ? "movie" : "series";
        if (Ignored.Contains(eventType) || ReadId(body, subject) is not { } arrId)
        {
            _logger.LogDebug("{Instance} webhook {EventType} needs no refresh", instance.DisplayName, eventType);
            return new WebhookOutcome(WebhookRejection.None, eventType, false);
        }

        var queued = _queue.Enqueue(new LibraryRefresh(instance.Key, arrId));
        if (queued)
        {
            _logger.LogInformation("{Instance} webhook {EventType} for {Subject} {Id}, refresh queued", instance.DisplayName, eventType, subject, arrId);
        }
        else
        {
            _logger.LogWarning("{Instance} webhook {EventType} dropped, the refresh queue is full. The next full sync picks it up.",
                instance.DisplayName, eventType);
        }

        return new WebhookOutcome(WebhookRejection.None, eventType, queued);
    }

    private static int? ReadId(JsonElement body, string subject)
    {
        if (!body.TryGetProperty(subject, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        if (!id.TryGetInt32(out var value) || value <= 0)
        {
            return null;
        }

        return value;
    }

    private static string? ReadPassword(string? authorization)
    {
        // Radarr and Sonarr offer a username and password on a webhook, sent as basic authentication.
        // The secret is the password, the username can be anything.
        if (authorization is null || !authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var encoded = authorization["Basic ".Length..].Trim();
            var bytes = Convert.FromBase64String(encoded);
            var decoded = Encoding.UTF8.GetString(bytes);
            var separator = decoded.IndexOf(':');
            if (separator < 0)
            {
                return null;
            }

            return decoded[(separator + 1)..];
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
