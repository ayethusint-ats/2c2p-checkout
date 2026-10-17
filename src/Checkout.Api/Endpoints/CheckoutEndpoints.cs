using System.Text.Json.Nodes;
using Checkout.Api.OpenApi;
using Checkout.Application.Checkout;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

namespace Checkout.Api.Endpoints;

public static class CheckoutEndpoints
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const int MaxIdempotencyKeyLength = 100;

    public static IEndpointRouteBuilder MapCheckoutEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/checkout", Checkout)
            .WithTags("Checkout")
            .WithName(nameof(Checkout))
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                // The parameter is nullable so we can return our own 400; still document it as required.
                if (operation.Parameters?.FirstOrDefault(p => p.Name == IdempotencyKeyHeader) is OpenApiParameter header)
                {
                    header.Required = true;
                    header.Example = OpenApiExamples.IdempotencyKey;
                    header.Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Format = "uuid",
                        MaxLength = MaxIdempotencyKeyLength,
                    };
                    header.Description =
                        "A new GUID for each checkout attempt, reused for every retry of that attempt. " +
                        "Same key again: the original response is returned and 2C2P is not called again. " +
                        "Same key while the first request is still running: the request waits briefly for its result, " +
                        "then answers 409 with Retry-After (retry with the same key). " +
                        "Same key for a different customer, product or card: 422.";
                }

                OpenApiExamples.SetRequestExamples(operation,
                    ("oneTime", "One-time product (2C2P payment page)",
                        new JsonObject { ["customerId"] = 1, ["productId"] = 1, ["customerCardId"] = null }),
                    ("recurring", "Recurring product (payment page; the card is saved)",
                        new JsonObject { ["customerId"] = 1, ["productId"] = 3, ["customerCardId"] = null }),
                    ("savedCard", "Pay with a saved card",
                        new JsonObject { ["customerId"] = 1, ["productId"] = 1, ["customerCardId"] = 1 }));

                OpenApiExamples.SetResponseExamples(operation, 200,
                    ("hostedPagePending", "Payment page created: send the customer to redirectUrl",
                        Result("Pending", OpenApiExamples.PaymentPageUrl)),
                    ("savedCardSucceeded", "Saved card charged",
                        Result("Succeeded", null)),
                    ("failed", "2C2P rejected the payment",
                        Result("Failed", null)));

                OpenApiExamples.SetResponseExample(operation, 400,
                    OpenApiExamples.ValidationProblem(IdempotencyKeyHeader, $"The {IdempotencyKeyHeader} header must be a GUID."));
                OpenApiExamples.SetResponseExample(operation, 404,
                    OpenApiExamples.Problem(404, "Resource not found", "Saved card 7 for customer 1 was not found."));
                return Task.CompletedTask;
            });

        return app;
    }

    private static JsonObject Result(string status, string? redirectUrl) => new()
    {
        ["invoiceNo"] = OpenApiExamples.InvoiceNo,
        ["status"] = status,
        ["redirectUrl"] = redirectUrl,
    };

    public sealed record CheckoutRequest(int CustomerId, int ProductId, int? CustomerCardId);

    [EndpointSummary("Start a checkout: 2C2P hosted payment page, or charge a saved card")]
    [EndpointDescription(
        "Creates a Pending transaction for the product's current price. " +
        "Without customerCardId: returns the 2C2P payment page URL as redirectUrl. " +
        "With customerCardId: charges that saved card. The result is Succeeded or Failed when 2C2P answers at once; " +
        "Pending with a redirectUrl when the customer must complete 3-D Secure; Pending without a redirectUrl when " +
        "the outcome is not known yet (poll GET /api/transactions/{invoiceNo}). " +
        "Status is Failed (with no redirectUrl) if 2C2P rejected the payment token request or could not be reached. " +
        "400: invalid input; errors names each invalid field. " +
        "404: unknown customer or product, or a card that does not belong to the customer. " +
        "409 (with Retry-After): another request with the same key is still running; retry with the same key. " +
        "422: the key was already used for a different customer, product or card.")]
    [ProducesResponseType<CheckoutResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    private static async Task<Results<Ok<CheckoutResult>, ValidationProblem>> Checkout(
        CheckoutRequest body,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        ICheckoutService checkout,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        Guid key = default;

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            errors[IdempotencyKeyHeader] = [$"The {IdempotencyKeyHeader} header is required."];
        else if (idempotencyKey.Length > MaxIdempotencyKeyLength)
            errors[IdempotencyKeyHeader] = [$"The {IdempotencyKeyHeader} header must be at most {MaxIdempotencyKeyLength} characters."];
        else if (!Guid.TryParse(idempotencyKey, out key))
            errors[IdempotencyKeyHeader] = [$"The {IdempotencyKeyHeader} header must be a GUID."];

        if (body.CustomerId <= 0)
            errors["customerId"] = ["customerId must be a positive integer."];
        if (body.ProductId <= 0)
            errors["productId"] = ["productId must be a positive integer."];
        if (body.CustomerCardId is <= 0)
            errors["customerCardId"] = ["customerCardId must be a positive integer when given."];

        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        // Canonical form ("d" = lowercase, hyphenated), so "{ABC…}" and "abc…" are the same key.
        var command = new CheckoutCommand(body.CustomerId, body.ProductId, body.CustomerCardId, key.ToString("D"));

        // Application exceptions become 404/409/422 in GlobalExceptionHandler.
        return TypedResults.Ok(await checkout.CheckoutAsync(command, ct));
    }
}
