using Checkout.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Checkout.Api.ErrorHandling;

/// <summary>
/// Turns unhandled exceptions into RFC 7807 responses. Known application exceptions map to
/// 404/501 with their (safe) message; anything else is a logged 500 with no details sent.
/// </summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, title, detail) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found", exception.Message),
            FeatureNotImplementedException => (StatusCodes.Status501NotImplemented, "Not implemented", exception.Message),
            // Unreadable or missing JSON body, or a parameter that can't be bound.
            BadHttpRequestException bad => (bad.StatusCode, "Invalid request", "The request body is missing or is not valid JSON."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null),
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception for {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = status;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }
}
