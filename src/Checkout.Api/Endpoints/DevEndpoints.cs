using Checkout.Application.Payments;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Checkout.Api.Endpoints;

// TODO(Step 5): delete this file and its MapDevEndpoints() call once checkout calls the provider for real.
/// <summary>Temporary, Development-only endpoints for poking the 2C2P sandbox by hand.</summary>
public static class DevEndpoints
{
    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dev/2c2p/payment-token", CreateSmokePaymentToken)
            .WithTags("Dev")
            .WithName(nameof(CreateSmokePaymentToken));

        return app;
    }

    public sealed record SmokePaymentTokenResponse(bool Succeeded, string RespCode, string RespDesc, string? RedirectUrl);

    [EndpointSummary("TEMPORARY: request a 2C2P sandbox payment token (10.00 SGD)")]
    [ProducesResponseType<SmokePaymentTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    private static async Task<Results<Ok<SmokePaymentTokenResponse>, ProblemHttpResult>> CreateSmokePaymentToken(
        PaymentIntent intent, IPaymentProvider provider, CancellationToken ct)
    {
        if (intent == PaymentIntent.SavedCard)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Intent not supported here",
                detail: "SavedCard needs a real customer token; use NewPaymentMethod or NewCardAndSave.");

        var request = new PaymentTokenRequest(
            InvoiceNo: $"SMOKE{DateTime.UtcNow.Ticks}",
            Description: "Smoke test",
            Amount: 10.00m,
            CurrencyCode: "SGD",
            FrontendReturnUrl: "https://example.com/payment/return",
            BackendReturnUrl: "https://example.com/payment/notify",
            Intent: intent);

        try
        {
            var result = await provider.CreatePaymentTokenAsync(request, ct);
            return TypedResults.Ok(new SmokePaymentTokenResponse(
                result.Succeeded, result.RespCode, result.RespDesc, result.RedirectUrl));
        }
        catch (PaymentProviderException ex)
        {
            // The message holds only the invoice number and failure kind, never secrets or tokens.
            return TypedResults.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Payment provider error",
                detail: ex.Message);
        }
    }
}
