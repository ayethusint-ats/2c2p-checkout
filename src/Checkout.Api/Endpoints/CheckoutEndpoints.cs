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
                    header.Description =
                        "A new GUID per checkout attempt. Retrying with the same key returns the original result " +
                        "instead of creating a second payment.";
                }
                return Task.CompletedTask;
            });

        return app;
    }

    public sealed record CheckoutRequest(int CustomerId, int ProductId, int? CustomerCardId);

    [EndpointSummary("Start a checkout: 2C2P hosted payment page, or charge a saved card")]
    [EndpointDescription(
        "Creates a Pending transaction for the product's current price. " +
        "Without customerCardId: returns the 2C2P payment page URL as redirectUrl. " +
        "With customerCardId: charges that saved card. The result is Succeeded or Failed when 2C2P answers at once; " +
        "Pending with a redirectUrl when the customer must complete 3-D Secure; Pending without a redirectUrl when " +
        "the outcome is not known yet (poll GET /api/transactions/{invoiceNo}). " +
        "Status is Failed (with no redirectUrl) if 2C2P rejected the payment token request or could not be reached. " +
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

        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        // Canonical form ("d" = lowercase, hyphenated), so "{ABC…}" and "abc…" are the same key.
        var command = new CheckoutCommand(body.CustomerId, body.ProductId, body.CustomerCardId, key.ToString("D"));

        // Application exceptions become 404/409/422 in GlobalExceptionHandler.
        return TypedResults.Ok(await checkout.CheckoutAsync(command, ct));
    }
}
