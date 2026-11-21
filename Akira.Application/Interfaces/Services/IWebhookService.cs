using System.Text.Json;
using Akira.Server.Webhooks;

namespace Akira.Server.Interfaces.Services;

public interface IWebhookService
{
    /// <summary>
    /// Checks a Radarr or Sonarr webhook against its instance secret and queues a refresh of the movie or series it names.
    /// </summary>
    WebhookOutcome Receive(string instanceKey, string? authorization, JsonElement body);
}
