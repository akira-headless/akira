namespace Akira.Server.Bridges;

public interface ISignedUrlResolver
{
    /// <summary>
    /// Reads a signed url's token, or null when it is not valid, expired, or its device or link was revoked.
    /// </summary>
    Task<SignedTarget?> ResolveAsync(string token, CancellationToken cancellationToken);
}
