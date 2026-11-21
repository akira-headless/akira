using System.Net;

namespace Akira.Infrastructure.Integrations;

public sealed class UpstreamException : Exception
{
    public string Upstream { get; }
    public UpstreamFailure Failure { get; }
    public HttpStatusCode? StatusCode { get; }
    public TimeSpan? RetryAfter { get; }

    public UpstreamException(
        string upstream,
        UpstreamFailure failure,
        string message,
        HttpStatusCode? statusCode = null,
        TimeSpan? retryAfter = null,
        Exception? innerException = null) : base(message, innerException)
    {
        Upstream = upstream;
        Failure = failure;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }
}
