using System.Text.Json.Nodes;
using Checkout.Application.Payments;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

namespace Checkout.Api.Endpoints;

public static class PaymentNotificationEndpoints
{
    public static IEndpointRouteBuilder MapPaymentNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/payments/2c2p/notification", ReceiveNotification)
            .WithTags("Payments")
            .WithName(nameof(ReceiveNotification))
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                if (operation.RequestBody?.Content?.TryGetValue("application/json", out var media) == true
                    && media is OpenApiMediaType json)
                {
                    json.Example = new JsonObject
                    {
                        ["payload"] = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJtZXJjaGFudElEIjoiSlQwMSIsImludm9pY2VObyI6IklOVjIwMjYxMDE1QTFCMkMzRDQiLCJhbW91bnQiOjg5LjksImN1cnJlbmN5Q29kZSI6IlNHRCIsInJlc3BDb2RlIjoiMDAwMCIsInJlc3BEZXNjIjoiU3VjY2VzcyJ9.<signature>",
                    };
                }
                return Task.CompletedTask;
            });

        return app;
    }

    public sealed record NotificationRequest(string? Payload);

    [EndpointSummary("2C2P backend payment notification (server to server)")]
    [EndpointDescription(
        "Called by 2C2P with the signed payment result. The payload JWT is verified with the merchant secret " +
        "before anything in it is read. Any verified notification is acknowledged with 200 and an empty body, " +
        "including duplicates, unknown invoices and amount mismatches (those are logged, not retried). " +
        "400 means the notification could not be verified; the reason is not disclosed.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    private static async Task<Results<Ok, ProblemHttpResult>> ReceiveNotification(
        NotificationRequest body,
        IPaymentProvider provider,
        IPaymentResultProcessor processor,
        CancellationToken ct)
    {
        // One generic answer for every rejection, so a caller can't probe which check failed.
        var result = string.IsNullOrEmpty(body.Payload) ? null : provider.VerifyAndParseNotification(body.Payload);
        if (result is null)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid notification",
                detail: "The notification could not be verified.");

        // The outcome is logged by the processor; 2C2P only needs to know we received it.
        await processor.ApplyAsync(result, ct);
        return TypedResults.Ok();
    }
}
