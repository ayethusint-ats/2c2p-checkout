namespace Checkout.Infrastructure.Payments.TwoCTwoP;

// Wire formats for the 2C2P Payment API v4.3. Property names become the JSON field
// names through the camelCase policy (MerchantID -> merchantID, Request3DS -> request3DS).

/// <summary>Outer body of every request and of signed responses: {"payload":"&lt;JWT&gt;"}.</summary>
internal sealed record JwtEnvelope(string Payload);

/// <summary>
/// Raw response body. Signed responses carry only Payload; some request errors come back
/// as plain JSON with RespCode/RespDesc and no Payload.
/// </summary>
internal sealed record ResponseBody(string? Payload, string? RespCode, string? RespDesc);

/// <summary>Claims of the Payment Token request JWT. Null fields are omitted.</summary>
internal sealed record PaymentTokenRequestPayload
{
    public required string MerchantID { get; init; }
    public required string InvoiceNo { get; init; }
    public required string Description { get; init; }
    public required decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }
    public required string FrontendReturnUrl { get; init; }
    public required string BackendReturnUrl { get; init; }

    public string[]? PaymentChannel { get; init; }
    public bool? Tokenize { get; init; }
    public string[]? CustomerToken { get; init; }
    public bool? CustomerTokenOnly { get; init; }
    public string? Request3DS { get; init; }
}

/// <summary>Claims of the Payment Token response JWT (only the fields we use).</summary>
internal sealed record PaymentTokenResponsePayload(
    string? PaymentToken,
    string? WebPaymentUrl,
    string? RespCode,
    string? RespDesc);
