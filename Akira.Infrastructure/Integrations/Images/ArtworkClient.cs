using Akira.Core.Library;
using Akira.Infrastructure.Interfaces.Integrations;

namespace Akira.Infrastructure.Integrations.Images;

public class ArtworkClient : IArtworkClient
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private const string Upstream = "Artwork host";

    private readonly HttpClient _httpClient;

    public ArtworkClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<ImageData> GetAsync(string url, CancellationToken cancellationToken)
    {
        // Checked again here and not only at sync time, so a row written by an older version or
        // edited by hand still cannot point the server at an arbitrary host
        var validated = Artwork.ValidateRemoteUrl(url)
            ?? throw new UpstreamException(Upstream, UpstreamFailure.NotFound, "The image url is not on an allowed artwork host.");

        try
        {
            return await UpstreamHttp.GetImageAsync(_httpClient, Upstream, validated, cancellationToken);
        }
        catch (UpstreamException exception) when (exception.Failure == UpstreamFailure.Unauthorized)
        {
            // No key is sent to an artwork host. TheTVDB answers a removed image with 403, so it is
            // a missing image and not a refusal that should hold back the images after it.
            throw new UpstreamException(Upstream, UpstreamFailure.NotFound, $"{Upstream} has no image at {validated}.",
                exception.StatusCode, innerException: exception);
        }
    }
}
