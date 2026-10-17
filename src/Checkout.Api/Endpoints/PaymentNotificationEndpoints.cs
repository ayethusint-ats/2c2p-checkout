using System.Text.Json.Nodes;
using Checkout.Api.OpenApi;
using Checkout.Application.Payments;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

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
                // Deliberately not a JWT: a real-looking token in docs gets copied, and could carry real data.
                OpenApiExamples.SetRequestExample(operation,
                    new JsonObject { ["payload"] = "<JWT signed by 2C2P with the merchant secret key>" });

                OpenApiExamples.SetResponseExamples(operation, 400,
                    ("missingPayload", "No payload in the body",
                        OpenApiExamples.ValidationProblem("payload", "payload is required.")),
                    ("notVerified", "Signature, merchant or content check failed",
                        OpenApiExamples.Problem(400, "Invalid notification", "The notification could not be verified.")));
                return Task.CompletedTask;
            });

        return app;
    }

    public sealed record NotificationRequest(string? Payload);

    [EndpointSummary("2C2P backend payment notification (server to server)")]
    [EndpointDescription(
        "Called by 2C2P, not by API clients: 2C2P posts the signed payment result here (the backendReturnUrl " +
        "sent with each payment). The payload JWT is verified with the merchant secret before anything in it " +
        "is read. Any verified notification is acknowledged with 200 and an empty body, including duplicates, " +
        "unknown invoices and amount mismatches (those are logged, not retried). " +
        "400: the payload is missing, or the notification could not be verified (the reason is not disclosed).")]
    [ProducesResponseType(StatusCodes.Status200OK, Description = "Received. Empty body.")]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    private static async Task<Results<Ok, ValidationProblem, ProblemHttpResult>> ReceiveNotification(
        NotificationRequest body,
        IPaymentProvider provider,
        IPaymentResultProcessor processor,
        CancellationToken ct)
    {
        // A missing payload is a malformed request, not a failed verification, so it says which field.
        if (string.IsNullOrWhiteSpace(body.Payload))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["payload"] = ["payload is required."],
            });

        // One generic answer for every verification failure, so a caller can't probe which check failed.
        var result = provider.VerifyAndParseNotification(body.Payload);
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
