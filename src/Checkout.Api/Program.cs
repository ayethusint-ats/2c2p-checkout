using Checkout.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Built-in .NET OpenAPI document generator (served at /openapi/v1.json).
builder.Services.AddOpenApi();

// Fail fast with a clear message rather than a MySQL connection error later.
var connectionString = builder.Configuration.GetConnectionString("Checkout")
    ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:Checkout' is not configured.");
builder.Services.AddInfrastructure(connectionString);

var app = builder.Build();

// Bring the schema up to date, then seed demo data, before accepting requests.
await app.Services.MigrateDatabaseAsync();

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

app.Run();

// Makes the implicit Program class visible to WebApplicationFactory<Program> in tests.
public partial class Program;
