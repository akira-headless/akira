namespace Akira.Infrastructure.Integrations;

public enum UpstreamFailure
{
    NotConfigured,
    Unauthorized,
    NotFound,
    RateLimited,
    Timeout,
    Unreachable,
    UnexpectedStatus,
    Malformed
}
