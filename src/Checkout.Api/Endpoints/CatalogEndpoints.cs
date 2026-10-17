using System.Text.Json.Nodes;
using Checkout.Api.OpenApi;
using Checkout.Application.Catalog;
using Checkout.Application.Common;
using Checkout.Application.Dtos;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Checkout.Api.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Catalog").RequireCors(CorsPolicies.Frontend);

        api.MapGet("/customers", GetCustomers)
            .WithName(nameof(GetCustomers))
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                OpenApiExamples.SetResponseExample(operation, 200, new JsonArray(
                    new JsonObject { ["id"] = 1, ["firstName"] = "Alice", ["lastName"] = "Tan", ["email"] = "alice.tan@example.com" },
                    new JsonObject { ["id"] = 2, ["firstName"] = "Ben", ["lastName"] = "Lim", ["email"] = "ben.lim@example.com" }));
                return Task.CompletedTask;
            });

        api.MapGet("/products", GetProducts)
            .WithName(nameof(GetProducts))
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                OpenApiExamples.SetResponseExample(operation, 200, new JsonArray(
                    Product(1, "Wireless Earbuds", "Bluetooth earbuds with charging case.", 89.90m, "OneTime"),
                    Product(3, "Cloud Storage Monthly", "200 GB cloud storage, billed monthly.", 4.99m, "Recurring")));
                return Task.CompletedTask;
            });

        // No ":int" route constraint: a non-numeric id is then a 400 (bad input), not a 404 (no such route).
        api.MapGet("/customers/{id}/cards", GetSavedCards)
            .WithName(nameof(GetSavedCards))
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                if (operation.Parameters?.FirstOrDefault(p => p.Name == "id") is Microsoft.OpenApi.OpenApiParameter customerId)
                {
                    customerId.Example = 1;
                    customerId.Description = "The customer's id (a positive integer).";
                }

                OpenApiExamples.SetResponseExample(operation, 200, new JsonArray(
                    new JsonObject { ["id"] = 1, ["maskedCardNo"] = OpenApiExamples.MaskedCardNo }));
                OpenApiExamples.SetResponseExample(operation, 400,
                    OpenApiExamples.ValidationProblem("id", "id must be a positive integer."));
                OpenApiExamples.SetResponseExample(operation, 404,
                    OpenApiExamples.Problem(404, "Resource not found", "Customer 99 was not found."));
                return Task.CompletedTask;
            });

        return app;
    }

    private static JsonObject Product(int id, string name, string description, decimal price, string paymentType) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["description"] = description,
        ["price"] = price,
        ["currency"] = "SGD",
        ["paymentType"] = paymentType,
    };

    [EndpointSummary("List all customers")]
    [EndpointDescription("Demo customers to check out as. Served from the Redis cache when possible (10-minute TTL).")]
    [ProducesResponseType<IReadOnlyList<CustomerDto>>(StatusCodes.Status200OK)]
    private static async Task<Ok<IReadOnlyList<CustomerDto>>> GetCustomers(
        CatalogService catalog, CancellationToken ct) =>
        TypedResults.Ok(await catalog.GetCustomersAsync(ct));

    [EndpointSummary("List all products")]
    [EndpointDescription(
        "Products with their price in SGD. paymentType OneTime opens the 2C2P payment page; Recurring also " +
        "saves the card for later payments. Served from the Redis cache when possible (10-minute TTL).")]
    [ProducesResponseType<IReadOnlyList<ProductDto>>(StatusCodes.Status200OK)]
    private static async Task<Ok<IReadOnlyList<ProductDto>>> GetProducts(
        CatalogService catalog, CancellationToken ct) =>
        TypedResults.Ok(await catalog.GetProductsAsync(ct));

    [EndpointSummary("List a customer's saved cards (masked numbers only)")]
    [EndpointDescription(
        "Cards saved from earlier Recurring payments, newest first. Only the masked number is returned; " +
        "the 2C2P customer token never leaves the server. Not cached, so a newly saved card appears at once. " +
        "Use a card's id as customerCardId in POST /api/checkout.")]
    [ProducesResponseType<IReadOnlyList<SavedCardDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    private static async Task<Results<Ok<IReadOnlyList<SavedCardDto>>, ValidationProblem>> GetSavedCards(
        int id, CatalogService catalog, CancellationToken ct)
    {
        if (id <= 0)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["id"] = ["id must be a positive integer."],
            });

        // NotFoundException becomes the same 404 shape as every other unknown resource.
        var cards = await catalog.GetSavedCardsAsync(id, ct) ?? throw new NotFoundException("Customer", id);
        return TypedResults.Ok(cards);
    }
}
