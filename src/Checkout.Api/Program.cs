using System.Text.Json.Serialization;
using Checkout.Api.Endpoints;
using Checkout.Api.ErrorHandling;
using Checkout.Application;
using Checkout.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Built-in .NET OpenAPI document generator (served at /openapi/v1.json).
builder.Services.AddOpenApi();

// RFC 7807 bodies for errors (adds traceId); the handler covers unhandled exceptions.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// "OneTime" instead of 0 in responses (and in the OpenAPI schema).
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services
    .AddApplication(builder.Configuration)
    .AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Bring the schema up to date, then seed demo data, before accepting requests.
await app.Services.MigrateDatabaseAsync();

// First in the pipeline so it catches exceptions from everything after it.
app.UseExceptionHandler();

// Only expose API docs in Development so production doesn't advertise its surface.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Swashbuckle is used only for the UI; it renders the document generated above.
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Checkout API v1");
        options.RoutePrefix = "swagger";
    });
}

// Liveness probe: answers "is the process up?" without touching MySQL or Redis.
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
    .WithName("GetHealth");

app.MapCatalogEndpoints();

app.Run();

// Makes the implicit Program class visible to WebApplicationFactory<Program> in tests.
public partial class Program;
