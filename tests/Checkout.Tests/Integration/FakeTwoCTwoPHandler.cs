using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Checkout.Tests.Integration;

/// <summary>
/// Stands in for the 2C2P sandbox at the HTTP level: production code (JWT signing, parsing, verification)
/// runs for real. Verifies request JWTs, answers with correctly signed responses, and counts calls.
/// </summary>
internal sealed class FakeTwoCTwoPHandler : HttpMessageHandler
{
    private int _paymentTokenCalls;
    private int _doPaymentCalls;
    private int _inquiryCalls;
    private int _sequence;

    public int PaymentTokenCalls => Volatile.Read(ref _paymentTokenCalls);
    public int DoPaymentCalls => Volatile.Read(ref _doPaymentCalls);
    public int InquiryCalls => Volatile.Read(ref _inquiryCalls);

    /// <summary>invoiceNo -> what we were asked to charge, for the inquiry answer.</summary>
    public ConcurrentDictionary<string, (decimal Amount, string Currency)> Invoices { get; } = new();

    /// <summary>Keeps the first request inside its lock long enough for the others to collide with it.</summary>
    public TimeSpan PaymentTokenDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Runs first with the endpoint ("paymentToken", "payment", "paymentInquiry") and the request
    /// (decoded JWT payload, or the plain JSON for Do Payment). Return null for the default behaviour.
    /// </summary>
    public Func<string, JsonElement, HttpResponseMessage?>? Override { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var endpoint = request.RequestUri!.Segments[^1];
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));

        switch (endpoint)
        {
            case "paymentToken":
            {
                Interlocked.Increment(ref _paymentTokenCalls);
                var claims = VerifyRequest(body.RootElement);
                await Task.Delay(PaymentTokenDelay, ct);
                if (Override?.Invoke(endpoint, claims) is { } custom)
                    return custom;

                var invoiceNo = claims.GetProperty("invoiceNo").GetString()!;
                Invoices[invoiceNo] = (claims.GetProperty("amount").GetDecimal(), claims.GetProperty("currencyCode").GetString()!);

                var token = $"PT_{Interlocked.Increment(ref _sequence)}_{Guid.NewGuid():N}";
                return Signed(new
                {
                    webPaymentUrl = $"https://sandbox-pgw-ui.2c2p.com/payment/4.3/#/token/{token}",
                    paymentToken = token,
                    respCode = "0000",
                    respDesc = "Success",
                });
            }

            case "payment":
            {
                Interlocked.Increment(ref _doPaymentCalls);
                // Do Payment is plain JSON both ways.
                if (Override?.Invoke(endpoint, body.RootElement.Clone()) is { } custom)
                    return custom;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { respCode = "2000", respDesc = "Transaction is completed" }),
                };
            }

            case "paymentInquiry":
            {
                Interlocked.Increment(ref _inquiryCalls);
                var claims = VerifyRequest(body.RootElement);
                if (Override?.Invoke(endpoint, claims) is { } custom)
                    return custom;

                var invoiceNo = claims.GetProperty("invoiceNo").GetString()!;
                var (amount, currency) = Invoices[invoiceNo];
                return Signed(new
                {
                    merchantID = IntegrationFixture.MerchantId,
                    invoiceNo,
                    amount,
                    currencyCode = currency,
                    channelCode = "CC",
                    accountNo = "411111XXXXXX1111",
                    tranRef = $"TR{Interlocked.Increment(ref _sequence)}",
                    respCode = "0000",
                    respDesc = "Success",
                });
            }

            default:
                return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    // A badly signed request is a production bug: fail loudly (it surfaces as a 500 in the test).
    private static JsonElement VerifyRequest(JsonElement body)
    {
        var jwt = body.GetProperty("payload").GetString()!;
        if (!TestJwt.TryVerify(jwt, IntegrationFixture.SecretKey, out var claims))
            throw new InvalidOperationException("Request JWT did not verify with the test secret.");
        return claims;
    }

    public static HttpResponseMessage Signed(object payload) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { payload = TestJwt.Sign(payload, IntegrationFixture.SecretKey) }),
    };
}
