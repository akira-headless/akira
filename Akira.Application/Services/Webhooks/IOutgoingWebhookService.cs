using Akira.Server.Models.Webhooks;

namespace Akira.Server.Services.Webhooks;

public interface IOutgoingWebhookService
{
    Task<IReadOnlyList<OutgoingWebhookSummary>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds a webhook that hears about events from now on, and returns its signing secret once.
    /// </summary>
    Task<ServiceResult<CreatedOutgoingWebhook>> CreateAsync(CreateOutgoingWebhookRequest request, CancellationToken cancellationToken);

    Task<ServiceResult<OutgoingWebhookSummary>> UpdateAsync(Guid id, UpdateOutgoingWebhookRequest request, CancellationToken cancellationToken);

    Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the signing secret and returns the new one once.
    /// </summary>
    Task<ServiceResult<CreatedOutgoingWebhook>> RotateSecretAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a webhook.test delivery now and reports what the receiver answered.
    /// </summary>
    Task<ServiceResult<OutgoingWebhookTestResult>> TestAsync(Guid id, CancellationToken cancellationToken);
}
