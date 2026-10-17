using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Checkout.Api.OpenApi;

/// <summary>
/// Example values shown in Swagger. All made up: no real secrets, tokens, customers or tunnel URLs.
/// </summary>
internal static class OpenApiExamples
{
    public const string InvoiceNo = "INV20261007000123";
    public const string IdempotencyKey = "3f2b8c4e-9a1d-4e7b-8c2a-5d6f7e8a9b0c";
    public const string MaskedCardNo = "411111XXXXXX1111";
    public const string PaymentPageUrl = "https://sandbox-pgw-ui.2c2p.com/payment/4.3/#/token/EXAMPLE-PAYMENT-TOKEN";

    // The W3C Trace Context specification's own example id.
    private const string TraceId = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00";

    public static JsonObject Problem(int status, string title, string detail) => new()
    {
        ["type"] = ProblemType(status),
        ["title"] = title,
        ["status"] = status,
        ["detail"] = detail,
        ["traceId"] = TraceId,
    };

    public static JsonObject ValidationProblem(string field, string message)
    {
        var problem = Problem(400, "One or more validation errors occurred.", "One or more fields are invalid. See errors for details.");
        problem["errors"] = new JsonObject { [field] = new JsonArray(message) };
        return problem;
    }

    /// <summary>Used for any error response an endpoint doesn't give its own example for.</summary>
    public static JsonObject DefaultError(int status) => status switch
    {
        400 => ValidationProblem("customerId", "customerId must be a positive integer."),
        404 => Problem(404, "Resource not found", "Product 99 was not found."),
        409 => Problem(409, "Checkout in progress",
            $"A checkout with Idempotency-Key {IdempotencyKey} is still in progress. Retry shortly; " +
            "if it never completes, start a new checkout with a new key."),
        422 => Problem(422, "Idempotency key reused",
            $"Idempotency-Key {IdempotencyKey} was already used for a different checkout. Use a new key."),
        _ => Problem(500, "Internal Server Error", "An unexpected error occurred."),
    };

    /// <summary>Sets one example on every media type of a response.</summary>
    public static void SetResponseExample(OpenApiOperation operation, int status, JsonNode example)
    {
        foreach (var media in ResponseMedia(operation, status))
            media.Example = example.DeepClone();
    }

    /// <summary>Sets several named examples (shown as a drop-down in Swagger UI) on a response.</summary>
    public static void SetResponseExamples(OpenApiOperation operation, int status, params (string Name, string Summary, JsonNode Value)[] examples)
    {
        foreach (var media in ResponseMedia(operation, status))
            media.Examples = ToExamples(examples);
    }

    public static void SetRequestExamples(OpenApiOperation operation, params (string Name, string Summary, JsonNode Value)[] examples)
    {
        foreach (var media in operation.RequestBody?.Content?.Values ?? [])
            media.Examples = ToExamples(examples);
    }

    public static void SetRequestExample(OpenApiOperation operation, JsonNode example)
    {
        foreach (var media in operation.RequestBody?.Content?.Values ?? [])
            media.Example = example.DeepClone();
    }

    public static string ProblemType(int status) => status switch
    {
        400 => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        404 => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        409 => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        422 => "https://tools.ietf.org/html/rfc9110#section-15.5.21",
        _ => "https://tools.ietf.org/html/rfc9110#section-15.6.1",
    };

    private static IEnumerable<OpenApiMediaType> ResponseMedia(OpenApiOperation operation, int status) =>
        operation.Responses?.TryGetValue(status.ToString(), out var response) == true
            ? response.Content?.Values ?? []
            : [];

    private static Dictionary<string, IOpenApiExample> ToExamples((string Name, string Summary, JsonNode Value)[] examples) =>
        examples.ToDictionary(
            e => e.Name,
            e => (IOpenApiExample)new OpenApiExample { Summary = e.Summary, Value = e.Value.DeepClone() });
}
