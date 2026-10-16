using Checkout.Application.Catalog;
using Checkout.Application.Dtos;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Checkout.Api.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Catalog");

        api.MapGet("/customers", GetCustomers).WithName(nameof(GetCustomers));
        api.MapGet("/products", GetProducts).WithName(nameof(GetProducts));
        api.MapGet("/customers/{id:int}/cards", GetSavedCards).WithName(nameof(GetSavedCards));

        return app;
    }

    [EndpointSummary("List all customers")]
    [ProducesResponseType<IReadOnlyList<CustomerDto>>(StatusCodes.Status200OK)]
    private static async Task<Ok<IReadOnlyList<CustomerDto>>> GetCustomers(
        CatalogService catalog, CancellationToken ct) =>
        TypedResults.Ok(await catalog.GetCustomersAsync(ct));

    [EndpointSummary("List all products")]
    [ProducesResponseType<IReadOnlyList<ProductDto>>(StatusCodes.Status200OK)]
    private static async Task<Ok<IReadOnlyList<ProductDto>>> GetProducts(
        CatalogService catalog, CancellationToken ct) =>
        TypedResults.Ok(await catalog.GetProductsAsync(ct));

    [EndpointSummary("List a customer's saved cards (masked numbers only)")]
    [ProducesResponseType<IReadOnlyList<SavedCardDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    private static async Task<Results<Ok<IReadOnlyList<SavedCardDto>>, ProblemHttpResult>> GetSavedCards(
        int id, CatalogService catalog, CancellationToken ct)
    {
        var cards = await catalog.GetSavedCardsAsync(id, ct);

        if (cards is null)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Customer not found",
                detail: $"Customer {id} does not exist.");

        return TypedResults.Ok(cards);
    }
}
