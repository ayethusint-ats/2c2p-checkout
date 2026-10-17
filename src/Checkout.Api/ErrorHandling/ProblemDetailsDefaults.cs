using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Mvc;

namespace Checkout.Api.ErrorHandling;

/// <summary>
/// Fills in what the framework leaves empty, so every error body has the same fields
/// (type, title, status, detail, traceId) whoever produced it.
/// </summary>
internal static class ProblemDetailsDefaults
{
    // RFC 9110 section for each status this API returns.
    private static readonly Dictionary<int, string> TypeByStatus = new()
    {
        [400] = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        [404] = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        [405] = "https://tools.ietf.org/html/rfc9110#section-15.5.6",
        [409] = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        [415] = "https://tools.ietf.org/html/rfc9110#section-15.5.16",
        [422] = "https://tools.ietf.org/html/rfc9110#section-15.5.21",
        [500] = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
    };

    /// <summary>Hooked into AddProblemDetails; runs for every problem response.</summary>
    public static void Apply(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var status = problem.Status ?? context.HttpContext.Response.StatusCode;
        problem.Status = status;

        if (problem.Type is null && TypeByStatus.TryGetValue(status, out var type))
            problem.Type = type;

        problem.Title ??= ReasonPhrases.GetReasonPhrase(status);
        problem.Detail ??= DefaultDetail(problem, status);

        // Correlates the response with the server log entry (W3C trace id when tracing is on).
        problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    }

    private static string DefaultDetail(ProblemDetails problem, int status) => status switch
    {
        400 when problem is HttpValidationProblemDetails => "One or more fields are invalid. See errors for details.",
        400 => "The request is invalid.",
        404 => "The requested resource does not exist.",
        405 => "This HTTP method is not supported for this resource.",
        415 => "The request body must be JSON (Content-Type: application/json).",
        500 => "An unexpected error occurred.",
        _ => "The request could not be processed.",
    };
}
