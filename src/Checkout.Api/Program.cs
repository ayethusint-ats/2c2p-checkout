using System.Text.Json.Serialization;
using Checkout.Api.Endpoints;
using Checkout.Api.ErrorHandling;
using Checkout.Api.OpenApi;
using Checkout.Application;
using Checkout.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Built-in .NET OpenAPI document generator (served at /openapi/v1.json); see OpenApi/.
builder.Services.AddCheckoutOpenApi();

// Every error body is RFC 7807 problem details with the same fields, whoever produced it:
// our endpoints, the exception handler, or the framework (404/405/415 via status code pages).
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsDefaults.Apply);
builder.Services.AddSingleton<IProblemDetailsWriter, JsonProblemDetailsWriter>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Unreadable bodies and unbindable route values throw (and reach GlobalExceptionHandler) in every
// environment. By default this only happens in Development; elsewhere they'd be empty 400s.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

// "OneTime" instead of 0 in responses (and in the OpenAPI schema).
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The frontend runs on another origin (http://localhost:3000), so the browser asks before calling us.
// Only the configured origins, methods and headers are allowed; never AllowAnyOrigin.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() is { Length: > 0 } origins
    ? origins
    : ["http://localhost:3000"];
builder.Services.AddCors(options => options.AddPolicy(CorsPolicies.Frontend, policy => policy
    .WithOrigins(allowedOrigins)
    .WithMethods("GET", "POST", "OPTIONS")
    .WithHeaders("Content-Type", "Idempotency-Key")
    // Without this the browser hides Retry-After from the frontend's JavaScript on a 409.
    .WithExposedHeaders("Retry-After")));

builder.Services
    .AddApplication(builder.Configuration)
    .AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Bring the schema up to date, then seed demo data, before accepting requests.
await app.Services.MigrateDatabaseAsync();

// First in the pipeline so it catches exceptions from everything after it.
app.UseExceptionHandler();

// Gives empty error responses (unknown route 404, 405, 415) a problem details body.
app.UseStatusCodePages();

// Applies the policy named by each endpoint's RequireCors (endpoints without one get no CORS headers).
app.UseCors();

// API docs are on in Development, and elsewhere only when Swagger:Enabled is explicitly true
// (docker-compose sets it). Off by default, so a production deployment doesn't advertise its surface.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.MapOpenApi();

    // Swashbuckle is used only for the UI; it renders the document generated above.
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Checkout API v1");
        options.RoutePrefix = "swagger";
    });
}

app.MapHealthEndpoints();
app.MapCatalogEndpoints();
app.MapCheckoutEndpoints();
app.MapPaymentNotificationEndpoints();
app.MapTransactionEndpoints();

app.Run();

// Makes the implicit Program class visible to WebApplicationFactory<Program> in tests.
public partial class Program;
