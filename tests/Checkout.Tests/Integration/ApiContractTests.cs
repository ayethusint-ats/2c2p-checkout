using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Checkout.Application.Checkout;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi;
using static Checkout.Tests.Integration.IntegrationHelpers;

namespace Checkout.Tests.Integration;

/// <summary>What API clients rely on: the OpenAPI document and the shape of every error.</summary>
[Collection(IntegrationCollection.Name)]
public class ApiContractTests(IntegrationFixture fixture)
{
    // ---- OpenAPI document ----

    // The exact document MapOpenApi serves at /openapi/v1.json. Read through the provider because the
    // tests run as "Testing", where the endpoint isn't mapped (it's exposed in Development only).
    private static async Task<(JsonNode Document, string Json)> GetOpenApiAsync(CheckoutApiFactory app)
    {
        var provider = app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var document = await provider.GetOpenApiDocumentAsync();

        await using var text = new StringWriter();
        document.SerializeAsV31(new OpenApiJsonWriter(text));
        var json = text.ToString();
        return (JsonNode.Parse(json)!, json);
    }

    private static IEnumerable<(string Name, JsonObject Operation)> Operations(JsonNode document) =>
        document["paths"]!.AsObject().SelectMany(path => path.Value!.AsObject()
            .Where(op => op.Key != "parameters")
            .Select(op => ($"{op.Key.ToUpperInvariant()} {path.Key}", op.Value!.AsObject())));

    [Fact]
    public async Task Every_operation_has_a_summary_a_tag_and_a_response_example()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var (document, _) = await GetOpenApiAsync(app);

        var operations = Operations(document).ToList();
        Assert.Equal(7, operations.Count);

        Assert.All(operations, op =>
        {
            var (name, operation) = op;
            Assert.False(string.IsNullOrWhiteSpace(operation["summary"]?.GetValue<string>()), $"{name}: summary");
            Assert.True(operation["tags"]?.AsArray().Count > 0, $"{name}: tag");

            var hasExample = operation["responses"]!.AsObject()
                .SelectMany(r => r.Value!["content"]?.AsObject() ?? [])
                .Any(media => media.Value!["example"] is not null || media.Value!["examples"] is not null);
            Assert.True(hasExample, $"{name}: response example");
        });
    }

    [Fact]
    public async Task Checkout_documents_a_required_idempotency_key_and_retry_after_on_409()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var (document, _) = await GetOpenApiAsync(app);
        var checkout = document["paths"]!["/api/checkout"]!["post"]!;

        var header = checkout["parameters"]!.AsArray()
            .Single(p => p!["name"]!.GetValue<string>() == "Idempotency-Key")!;
        Assert.Equal("header", header["in"]!.GetValue<string>());
        Assert.True(header["required"]!.GetValue<bool>());
        Assert.True(Guid.TryParse(header["example"]!.GetValue<string>(), out _));

        Assert.NotNull(checkout["responses"]!["409"]!["headers"]?["Retry-After"]);
    }

    [Fact]
    public async Task OpenApi_document_contains_no_configured_secrets()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var (_, json) = await GetOpenApiAsync(app);

        Assert.DoesNotContain(IntegrationFixture.SecretKey, json);
        Assert.DoesNotContain(IntegrationFixture.MerchantId, json);
    }

    // ---- error shape ----

    private static HttpRequestMessage BadRequest(string scenario) => scenario switch
    {
        "malformed JSON" => Checkout("{\"customerId\":", NewKey()),
        "missing customerId" => Checkout("{\"productId\":1}", NewKey()),
        "Idempotency-Key not a GUID" => Checkout("{\"customerId\":1,\"productId\":1}", "not-a-guid"),
        "unknown route" => new HttpRequestMessage(HttpMethod.Get, "/api/does-not-exist"),
        "unsupported method" => new HttpRequestMessage(HttpMethod.Delete, "/api/checkout"),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    private static HttpRequestMessage Checkout(string json, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    [Theory]
    [InlineData("malformed JSON", HttpStatusCode.BadRequest)]
    [InlineData("missing customerId", HttpStatusCode.BadRequest)]
    [InlineData("Idempotency-Key not a GUID", HttpStatusCode.BadRequest)]
    [InlineData("unknown route", HttpStatusCode.NotFound)]
    [InlineData("unsupported method", HttpStatusCode.MethodNotAllowed)]
    public async Task Errors_are_problem_details_with_type_title_status_and_trace_id(string scenario, HttpStatusCode expected)
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());

        var response = await app.CreateClient().SendAsync(BadRequest(scenario));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.False(string.IsNullOrEmpty(problem["type"]?.GetValue<string>()));
        Assert.False(string.IsNullOrEmpty(problem["title"]?.GetValue<string>()));
        Assert.Equal((int)expected, problem["status"]?.GetValue<int>());
        Assert.False(string.IsNullOrEmpty(problem["detail"]?.GetValue<string>()));
        Assert.False(string.IsNullOrEmpty(problem["traceId"]?.GetValue<string>()));
    }

    [Theory]
    [InlineData("customerCardId")]
    [InlineData("id")]
    [InlineData("payload")]
    public async Task Validation_errors_name_the_offending_field(string field)
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var client = app.CreateClient();

        var response = field switch
        {
            "customerCardId" => await client.SendAsync(Checkout("{\"customerId\":1,\"productId\":1,\"customerCardId\":0}", NewKey())),
            "id" => await client.GetAsync("/api/customers/0/cards"),
            _ => await PostNotification(client, new { payload = "" }),
        };

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.NotNull(problem["errors"]?[field]);
    }

    // ---- unhandled exceptions ----

    private sealed class ThrowingCheckoutService : ICheckoutService
    {
        public Task<CheckoutResult> CheckoutAsync(CheckoutCommand command, CancellationToken ct) =>
            throw new InvalidOperationException("SECRET-DETAIL: Server=db;Password=hunter2");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("text/html")]   // a browser: must still get JSON, never an HTML error page
    public async Task Unhandled_exception_is_a_generic_500_without_details(string? accept)
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler(), services =>
        {
            services.RemoveAll<ICheckoutService>();
            services.AddScoped<ICheckoutService, ThrowingCheckoutService>();
        });
        var request = Checkout("{\"customerId\":1,\"productId\":1}", NewKey());
        if (accept is not null)
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

        var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SECRET-DETAIL", body);
        Assert.DoesNotContain("hunter2", body);
        Assert.DoesNotContain("at ", body);   // no stack frames ("   at Namespace.Method(...)")

        var problem = JsonNode.Parse(body)!;
        Assert.Equal("An unexpected error occurred.", problem["detail"]?.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(problem["traceId"]?.GetValue<string>()));
    }
}
