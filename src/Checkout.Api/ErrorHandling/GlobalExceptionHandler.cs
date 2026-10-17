using System.Text.Json;
using Checkout.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Checkout.Api.ErrorHandling;

/// <summary>
/// Turns exceptions into RFC 7807 responses. Known application exceptions map to 404/409/422 with their
/// (safe, written by us) message; a request that can't be read is a 400; anything else is a logged 500
/// with a generic detail. Exception messages from the framework or libraries are never sent: they can
/// contain type names, SQL or values from the request.
/// </summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var problem = exception switch
        {
            NotFoundException => Problem(StatusCodes.Status404NotFound, "Resource not found", exception.Message),
            CheckoutInProgressException => Problem(StatusCodes.Status409Conflict, "Checkout in progress", exception.Message),
            IdempotencyKeyReuseException => Problem(StatusCodes.Status422UnprocessableEntity, "Idempotency key reused", exception.Message),
            // The request couldn't be read: bad JSON, a missing body, or a route value of the wrong type.
            BadHttpRequestException bad => InvalidRequest(bad),
            _ => Problem(StatusCodes.Status500InternalServerError, "Internal Server Error", "An unexpected error occurred."),
        };

        if (problem.Status == StatusCodes.Status500InternalServerError)
            // The full exception goes to the log only; the response carries just the traceId to find it.
            logger.LogError(exception, "Unhandled exception for {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = problem.Status!.Value;

        // Tell the client when to retry with the same key.
        if (exception is CheckoutInProgressException)
            httpContext.Response.Headers.RetryAfter = "2";

        // JsonProblemDetailsWriter accepts every request, so this always writes a body.
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };

    // Names the JSON field when System.Text.Json reports one (e.g. "customerId": "abc"),
    // without passing on its message, which names .NET types.
    private static ProblemDetails InvalidRequest(BadHttpRequestException bad)
    {
        if (bad.InnerException is JsonException { Path: { Length: > 2 } path })
        {
            var field = path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path;
            return new HttpValidationProblemDetails(new Dictionary<string, string[]>
            {
                [field] = ["The value is missing, malformed or of the wrong type."],
            })
            {
                Status = bad.StatusCode,
                Title = "Invalid request",
                Detail = "The request body is not valid JSON for this endpoint. See errors for details.",
            };
        }

        return Problem(bad.StatusCode, "Invalid request",
            bad.InnerException is JsonException
                ? "The request body is not valid JSON."
                : "The request body is missing, or a value in the route or headers could not be read.");
    }
}
