var builder = WebApplication.CreateBuilder(args);

// Built-in .NET OpenAPI document generator (served at /openapi/v1.json).
builder.Services.AddOpenApi();

var app = builder.Build();

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
