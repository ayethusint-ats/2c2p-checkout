using System.Net;
using System.Net.Http.Json;
using static Checkout.Tests.Integration.IntegrationHelpers;

namespace Checkout.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class CorsTests(IntegrationFixture fixture)
{
    private const string Frontend = "http://localhost:3000";

    // What the browser sends before a cross-origin POST with custom headers.
    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/checkout");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "idempotency-key,content-type");
        return request;
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : "";

    [Fact]
    public async Task Preflight_from_the_frontend_allows_the_checkout_headers()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());

        var response = await app.CreateClient().SendAsync(Preflight(Frontend));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(Frontend, Header(response, "Access-Control-Allow-Origin"));
        Assert.Contains("POST", Header(response, "Access-Control-Allow-Methods"));
        var allowedHeaders = Header(response, "Access-Control-Allow-Headers").ToLowerInvariant();
        Assert.Contains("idempotency-key", allowedHeaders);
        Assert.Contains("content-type", allowedHeaders);
    }

    [Fact]
    public async Task Preflight_from_an_unknown_origin_is_not_allowed()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());

        var response = await app.CreateClient().SendAsync(Preflight("https://evil.example"));

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Error_responses_keep_the_cors_headers_so_the_frontend_can_read_them()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { customerId = 999, productId = Earbuds }),
        };
        request.Headers.Add("Origin", Frontend);
        request.Headers.Add("Idempotency-Key", NewKey());

        var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(Frontend, Header(response, "Access-Control-Allow-Origin"));
        Assert.Contains("Retry-After", Header(response, "Access-Control-Expose-Headers"));
    }
}
