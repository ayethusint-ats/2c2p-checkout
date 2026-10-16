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

/// <summary>
/// Do Payment request. Unlike the other APIs this is plain JSON, not a JWT:
/// {"paymentToken":"…","locale":"en","payment":{"code":{"channelCode":"CC"},"data":{"customerToken":"…"}}}
/// </summary>
internal sealed record DoPaymentRequest(string PaymentToken, string Locale, DoPaymentMethod Payment);

internal sealed record DoPaymentMethod(DoPaymentCode Code, DoPaymentData Data);

internal sealed record DoPaymentCode(string ChannelCode);

internal sealed record DoPaymentData(string CustomerToken);

/// <summary>Do Payment response (plain, or the decoded JWT payload). Data is the redirect URL for 1000/1001.</summary>
internal sealed record DoPaymentResponse(string? RespCode, string? RespDesc, string? Data);

/// <summary>Claims of the Payment Inquiry request JWT.</summary>
internal sealed record PaymentInquiryRequestPayload(string MerchantID, string InvoiceNo, string Locale);
