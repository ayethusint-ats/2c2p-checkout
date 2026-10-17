using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Checkout.Api.OpenApi;

internal static class OpenApiSetup
{
    public static IServiceCollection AddCheckoutOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            // Enums are serialized as names (JsonStringEnumConverter); say so explicitly in the schema.
            options.AddSchemaTransformer((schema, context, _) =>
            {
                var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
                if (type.IsEnum)
                    schema.Type = type == context.JsonTypeInfo.Type
                        ? JsonSchemaType.String
                        : JsonSchemaType.String | JsonSchemaType.Null;
                return Task.CompletedTask;
            });

            options.AddDocumentTransformer<CommonResponsesTransformer>();
        });
}

/// <summary>
/// What every operation shares, applied after the endpoints' own documentation: the 500 response,
/// an example for each error response that has none, and the Retry-After header on 409.
/// </summary>
internal sealed class CommonResponsesTransformer : IOpenApiDocumentTransformer
{
    private static readonly int[] ErrorStatuses = [400, 404, 409, 422, 500];

    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info.Title = "Checkout API";
        document.Info.Description =
            "Checkout backend for the 2C2P sandbox (Payment API v4.3, SGD). Every error response is " +
            "RFC 7807 problem details (application/problem+json) with a traceId for support.";

        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = "Catalog", Description = "Customers, products and saved cards (read-only)." },
            new() { Name = "Checkout", Description = "Start a payment, safely retryable with an Idempotency-Key." },
            new() { Name = "Payments", Description = "Called by 2C2P, not by API clients." },
            new() { Name = "Transactions", Description = "Payment status." },
            new() { Name = "Health", Description = "Liveness probe." },
        };

        // Any operation can fail unexpectedly, so 500 is documented everywhere with the ProblemDetails schema.
        var problemSchema = await context.GetOrCreateSchemaAsync(typeof(ProblemDetails), cancellationToken: ct);
        document.AddComponent("ProblemDetails", problemSchema);

        foreach (var operation in document.Paths.Values.SelectMany(p => p.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>()))
        {
            operation.Responses ??= new OpenApiResponses();
            if (!operation.Responses.ContainsKey("500"))
                operation.Responses["500"] = new OpenApiResponse
                {
                    Description = "Unexpected server error. The detail is always generic; use traceId to find the log entry.",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/problem+json"] = new() { Schema = new OpenApiSchemaReference("ProblemDetails", document) },
                    },
                };

            foreach (var status in ErrorStatuses)
            {
                if (!operation.Responses.TryGetValue(status.ToString(), out var response))
                    continue;

                foreach (var media in response.Content?.Values ?? [])
                    if (media.Example is null && media.Examples is not { Count: > 0 })
                        media.Example = OpenApiExamples.DefaultError(status);
            }

            if (operation.Responses.TryGetValue("409", out var conflict) && conflict is OpenApiResponse conflictResponse)
            {
                // Declared by the endpoint as a 409; the header is the part a client must act on.
                conflictResponse.Headers ??= new Dictionary<string, IOpenApiHeader>();
                conflictResponse.Headers["Retry-After"] = new OpenApiHeader
                {
                    Description = "Seconds to wait before retrying with the same Idempotency-Key.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
                    Example = 2,
                };
            }
        }
    }
}
