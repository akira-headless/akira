namespace Akira.Server.Services;

/// <summary>
/// Represents the result of a service call that can fail with a ServiceError. Error is null on success, and RetryAfter
/// says when to try again after a PIN lockout or a busy transcoder.
/// </summary>
public sealed record ServiceResult<T>(T? Value, ServiceError? Error, string? Detail, TimeSpan? RetryAfter = null)
{
    public bool Succeeded => Error is null;

    public static ServiceResult<T> Ok(T value) => new(value, null, null);

    public static ServiceResult<T> Fail(ServiceError error, string detail, TimeSpan? retryAfter = null) =>
        new(default, error, detail, retryAfter);
}
