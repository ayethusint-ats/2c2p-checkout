using Checkout.Application.Dtos;
using Checkout.Application.Transactions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Checkout.Api.Endpoints;

public static class TransactionEndpoints
{
    // Matches the invoice_no column; anything longer cannot exist, so it is rejected before a query.
    private const int MaxInvoiceNoLength = 50;

    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/transactions/{invoiceNo}", GetTransaction)
            .WithTags("Transactions")
            .WithName(nameof(GetTransaction));

        return app;
    }

    [EndpointSummary("Get a transaction's current status")]
    [EndpointDescription(
        "Returns the stored status of a payment. A Pending saved-card payment is first checked with 2C2P " +
        "(Payment Inquiry), so polling this endpoint completes it; hosted-page payments are completed by the " +
        "2C2P notification. Never cached (Cache-Control: no-store). 404: unknown invoice number.")]
    [ProducesResponseType<TransactionStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    private static async Task<Results<Ok<TransactionStatusDto>, ValidationProblem>> GetTransaction(
        string invoiceNo,
        ITransactionStatusService statusService,
        HttpResponse response,
        CancellationToken ct)
    {
        if (invoiceNo.Length > MaxInvoiceNoLength)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["invoiceNo"] = [$"invoiceNo must be at most {MaxInvoiceNoLength} characters."],
            });

        // NotFoundException becomes 404 in GlobalExceptionHandler.
        var status = await statusService.GetAsync(invoiceNo, ct);

        // The status changes over time, so no browser or proxy may answer from a stored copy.
        response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(status);
    }
}
