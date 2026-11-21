using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Akira.Server.Services;

namespace Akira.Server.Endpoints;

internal static class ServiceResults
{
    public static ProblemHttpResult Problem<T>(ServiceResult<T> result, HttpContext httpContext)
    {
        var (status, title) = result.Error switch
        {
            ServiceError.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            ServiceError.Forbidden => (StatusCodes.Status403Forbidden, "Not allowed"),
            ServiceError.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            ServiceError.Invalid => (StatusCodes.Status400BadRequest, "Invalid request"),
            ServiceError.WrongPin => (StatusCodes.Status403Forbidden, "Wrong PIN"),
            ServiceError.LockedOut => (StatusCodes.Status429TooManyRequests, "Locked out"),
            ServiceError.Gone => (StatusCodes.Status410Gone, "No longer available"),
            ServiceError.TooMany => (StatusCodes.Status429TooManyRequests, "Too many requests"),
            ServiceError.Unavailable => (StatusCodes.Status503ServiceUnavailable, "Unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error")
        };

        if (result.RetryAfter is { } retryAfter)
        {
            httpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        return TypedResults.Problem(title: title, detail: result.Detail, statusCode: status);
    }
}
