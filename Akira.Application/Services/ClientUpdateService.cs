using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Updates;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Files;
using Akira.Server.Models.Updates;

namespace Akira.Server.Services;

public class ClientUpdateService : IClientUpdateService
{
    private readonly ClientUpdateStore _store;

    public ClientUpdateService(ClientUpdateStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public ClientUpdateModel? Get(string platform) => _store.Find(platform) is { } stored ? Model(stored) : null;

    /// <inheritdoc />
    public async Task<ServiceResult<ClientUpdateModel>> CheckAsync(string platform, CancellationToken cancellationToken)
    {
        try
        {
            return await _store.CheckAsync(platform, cancellationToken) is { } stored
                ? ServiceResult<ClientUpdateModel>.Ok(Model(stored))
                : ServiceResult<ClientUpdateModel>.Fail(ServiceError.NotFound, $"This server has no update source for the {platform} app.");
        }
        catch (Exception exception) when (exception is UpstreamException or IOException or UnauthorizedAccessException)
        {
            return ServiceResult<ClientUpdateModel>.Fail(ServiceError.Unavailable, exception.Message);
        }
    }

    private static ClientUpdateModel Model(StoredClientUpdate stored) =>
        new(stored.Platform, stored.Build, stored.Version, stored.FileName, stored.SizeBytes, stored.Sha256, stored.FetchedAt,
            ClientUpdateRoutes.FileUrl(stored.Platform));

    /// <inheritdoc />
    public FileDelivery Deliver(string platform)
    {
        if (_store.Find(platform) is not { } stored)
        {
            return new FileDelivery(DeliveryStatus.NotFound);
        }

        // A newer build may have replaced it since
        var info = new FileInfo(stored.Path);
        if (!info.Exists)
        {
            return new FileDelivery(DeliveryStatus.NotFound);
        }

        var lastModified = new DateTimeOffset(info.LastWriteTimeUtc);
        return new FileDelivery(DeliveryStatus.Ready, stored.Path, ContentType(stored.FileName))
        {
            Length = info.Length,
            LastModified = lastModified,
            EntityTag = $"\"{stored.Sha256}\""
        };
    }

    private static string ContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".apk" => "application/vnd.android.package-archive",
        ".flatpak" => "application/vnd.flatpak",
        _ => "application/octet-stream"
    };
}
