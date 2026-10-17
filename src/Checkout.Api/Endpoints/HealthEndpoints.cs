using System.Text.Json.Nodes;
using Checkout.Api.OpenApi;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Checkout.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", GetHealth)
            .WithTags("Health")
            .WithName(nameof(GetHealth))
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                OpenApiExamples.SetResponseExample(operation, 200, new JsonObject { ["status"] = "Healthy" });
                return Task.CompletedTask;
            });

        return app;
    }

    public sealed record HealthResponse(string Status);

    [EndpointSummary("Liveness probe")]
    [EndpointDescription(
        "Answers \"is the process up?\" without touching MySQL or Redis, so a slow dependency doesn't " +
        "make the orchestrator restart a healthy process.")]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    private static Ok<HealthResponse> GetHealth() => TypedResults.Ok(new HealthResponse("Healthy"));
}
