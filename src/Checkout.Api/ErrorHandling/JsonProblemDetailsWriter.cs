using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Checkout.Api.ErrorHandling;

/// <summary>
/// Fallback writer used when the built-in one declines, which it does when the client's Accept header
/// doesn't include JSON (e.g. a browser asking for text/html). Without it, such a request would get a
/// plain-text status page, or in Development the developer exception page with a stack trace.
/// This is a JSON API, so errors are always problem+json.
/// </summary>
internal sealed class JsonProblemDetailsWriter(IOptions<JsonOptions> jsonOptions) : IProblemDetailsWriter
{
    public bool CanWrite(ProblemDetailsContext context) => true;

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        // Same defaults (type, title, detail, traceId) as every other problem response.
        ProblemDetailsDefaults.Apply(context);

        var problem = context.ProblemDetails;
        return new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
            problem, problem.GetType(), jsonOptions.Value.SerializerOptions, "application/problem+json"));
    }
}
