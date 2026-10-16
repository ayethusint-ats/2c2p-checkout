using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Checkout.Application.Payments;
using Checkout.Infrastructure.Payments.TwoCTwoP;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Checkout.Tests.Payments.TwoCTwoP;

public class TwoCTwoPPaymentProviderTests
{
    private const string MerchantId = "JT01";
    private const string Secret = "1F4EDB965BBB3094F791A83750FBDCDA3852131CB5218E21E4F42929A1975E60";
    private const string BaseUrl = "https://sandbox-pgw.2c2p.com/payment/4.3/";

    private static readonly string[] IntentSpecificFields =
        ["paymentChannel", "tokenize", "customerToken", "customerTokenOnly", "request3DS"];

    // ---- request payload per intent ----

    [Fact]
    public async Task NewPaymentMethod_sends_common_fields_and_no_payment_channel()
    {
        var (provider, handler) = Create(_ => Signed(SuccessPayload));

        await provider.CreatePaymentTokenAsync(Request(PaymentIntent.NewPaymentMethod), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(new Uri(BaseUrl + "paymentToken"), handler.Request.RequestUri);

        var payload = DecodeRequestPayload(handler);
        AssertCommonFields(payload);
        foreach (var field in IntentSpecificFields)
            Assert.False(payload.TryGetProperty(field, out _), $"'{field}' should be absent");
    }

    [Fact]
    public async Task NewCardAndSave_sends_card_channel_and_tokenize()
    {
        var (provider, handler) = Create(_ => Signed(SuccessPayload));

        await provider.CreatePaymentTokenAsync(Request(PaymentIntent.NewCardAndSave), CancellationToken.None);

        var payload = DecodeRequestPayload(handler);
        AssertCommonFields(payload);
        Assert.Equal(["CC"], StringArray(payload, "paymentChannel"));
        Assert.True(payload.GetProperty("tokenize").GetBoolean());
        Assert.False(payload.TryGetProperty("customerToken", out _));
        Assert.False(payload.TryGetProperty("customerTokenOnly", out _));
        Assert.False(payload.TryGetProperty("request3DS", out _));
    }

    [Fact]
    public async Task SavedCard_sends_customer_token_only_without_3ds()
    {
        var (provider, handler) = Create(_ => Signed(SuccessPayload));

        await provider.CreatePaymentTokenAsync(
            Request(PaymentIntent.SavedCard, customerToken: "CUST_TOKEN_123"), CancellationToken.None);

        var payload = DecodeRequestPayload(handler);
        AssertCommonFields(payload);
        Assert.Equal(["CC"], StringArray(payload, "paymentChannel"));
        Assert.Equal(["CUST_TOKEN_123"], StringArray(payload, "customerToken"));
        Assert.True(payload.GetProperty("customerTokenOnly").GetBoolean());
        Assert.Equal("N", payload.GetProperty("request3DS").GetString());
        Assert.False(payload.TryGetProperty("tokenize", out _));
    }

    // ---- response handling ----

    [Fact]
    public async Task Response_0000_is_a_success_with_token_and_redirect_url()
    {
        var (provider, _) = Create(_ => Signed(SuccessPayload));

        var result = await provider.CreatePaymentTokenAsync(Request(PaymentIntent.NewPaymentMethod), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("kSAops9Zwhos8hSTSeLTUcCnr6", result.PaymentToken);
        Assert.Equal("https://sandbox-pgw-ui.2c2p.com/payment/4.3/#/token/kSAops9Zwhos8hSTSeLTUcCnr6", result.RedirectUrl);
        Assert.Equal("0000", result.RespCode);
        Assert.Equal("Success", result.RespDesc);
    }

    [Fact]
    public async Task Signed_non_0000_response_is_a_failed_result_not_an_exception()
    {
        var (provider, _) = Create(_ => Signed(new { respCode = "9042", respDesc = "Invalid currency code." }));

        var result = await provider.CreatePaymentTokenAsync(Request(PaymentIntent.NewPaymentMethod), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.PaymentToken);
        Assert.Null(result.RedirectUrl);
        Assert.Equal("9042", result.RespCode);
        Assert.Equal("Invalid currency code.", result.RespDesc);
    }

    [Fact]
    public async Task Response_signed_with_the_wrong_key_throws()
    {
        var (provider, _) = Create(_ => Signed(SuccessPayload, secret: "attacker-key"));

        await Assert.ThrowsAsync<PaymentProviderException>(() =>
            provider.CreatePaymentTokenAsync(Request(PaymentIntent.NewPaymentMethod), CancellationToken.None));
    }

    [Fact]
    public async Task Non_2xx_status_throws()
    {
        var (provider, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        await Assert.ThrowsAsync<PaymentProviderException>(() =>
            provider.CreatePaymentTokenAsync(Request(PaymentIntent.NewPaymentMethod), CancellationToken.None));
    }

    [Fact]
    public async Task Timeout_throws()
    {
        // What HttpClient raises when its Timeout elapses.
        var (provider, _) = Create(_ => throw new TaskCanceledException("timeout", new TimeoutException()));

        await Assert.ThrowsAsync<PaymentProviderException>(() =>
            provider.CreatePaymentTokenAsync(Request(PaymentIntent.NewPaymentMethod), CancellationToken.None));
    }

    [Fact]
    public async Task Unsigned_error_response_is_a_failed_result()
    {
        var (provider, _) = Create(_ => Json(new { respCode = "9015", respDesc = "Merchant not found." }));

        var result = await provider.CreatePaymentTokenAsync(Request(PaymentIntent.NewPaymentMethod), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("9015", result.RespCode);
    }

    [Fact]
    public async Task Unsigned_success_response_is_never_trusted()
    {
        var (provider, _) = Create(_ => Json(new { respCode = "0000", respDesc = "Success", paymentToken = "x", webPaymentUrl = "https://evil" }));

        await Assert.ThrowsAsync<PaymentProviderException>(() =>
            provider.CreatePaymentTokenAsync(Request(PaymentIntent.NewPaymentMethod), CancellationToken.None));
    }

    // ---- backend notification ----

    private static string SignedNotification(object payload, string secret = Secret) =>
        Hs256Jwt.Sign(JsonSerializer.Serialize(payload), secret);

    private static object NotificationPayload(object amount, string merchantId = MerchantId) => new
    {
        merchantID = merchantId,
        invoiceNo = "INV20261015A1B2C3D4",
        accountNo = "411111XXXXXX1111",
        amount,
        currencyCode = "SGD",
        tranRef = "4589123",
        channelCode = "VI",
        respCode = "0000",
        respDesc = "Success",
        customerToken = "CUST_TOKEN_ABC",
    };

    [Theory]
    [InlineData("89.90")]
    [InlineData(89.90)]
    public void Notification_with_valid_signature_is_parsed(object amount)
    {
        var (provider, _) = Create(_ => throw new InvalidOperationException("no HTTP for notifications"));

        var result = provider.VerifyAndParseNotification(SignedNotification(NotificationPayload(amount)));

        Assert.NotNull(result);
        Assert.Equal(new PaymentResultInfo(MerchantId, "INV20261015A1B2C3D4", 89.90m, "SGD", "0000", "Success",
            "VI", "411111XXXXXX1111", "4589123", "CUST_TOKEN_ABC"), result);
    }

    [Fact]
    public void Notification_without_optional_fields_is_parsed_with_nulls()
    {
        var (provider, _) = Create(_ => throw new InvalidOperationException());
        var jwt = SignedNotification(new
        {
            merchantID = MerchantId, invoiceNo = "INV1", amount = "10.00", currencyCode = "SGD",
            respCode = "4012", respDesc = "Declined",
        });

        var result = provider.VerifyAndParseNotification(jwt);

        Assert.NotNull(result);
        Assert.Equal(10.00m, result.Amount);
        Assert.Null(result.ChannelCode);
        Assert.Null(result.AccountNo);
        Assert.Null(result.TranRef);
        Assert.Null(result.CustomerToken);
    }

    [Fact]
    public void Notification_signed_with_wrong_key_is_rejected()
    {
        var (provider, _) = Create(_ => throw new InvalidOperationException());

        Assert.Null(provider.VerifyAndParseNotification(SignedNotification(NotificationPayload("89.90"), "attacker-key")));
    }

    [Fact]
    public void Tampered_notification_is_rejected()
    {
        var (provider, _) = Create(_ => throw new InvalidOperationException());
        var parts = SignedNotification(NotificationPayload("89.90")).Split('.');
        var forged = System.Buffers.Text.Base64Url.EncodeToString(
            System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(NotificationPayload("0.01"))));

        Assert.Null(provider.VerifyAndParseNotification($"{parts[0]}.{forged}.{parts[2]}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("a.b.c")]
    [InlineData("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..")]
    public void Garbage_notification_is_rejected(string payload)
    {
        var (provider, _) = Create(_ => throw new InvalidOperationException());

        Assert.Null(provider.VerifyAndParseNotification(payload));
    }

    [Fact]
    public void Notification_for_another_merchant_is_rejected()
    {
        var (provider, _) = Create(_ => throw new InvalidOperationException());

        Assert.Null(provider.VerifyAndParseNotification(SignedNotification(NotificationPayload("89.90", merchantId: "OTHER"))));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1,000.00")]
    [InlineData("-5.00")]
    [InlineData(true)]
    public void Notification_with_unparseable_amount_is_rejected(object amount)
    {
        var (provider, _) = Create(_ => throw new InvalidOperationException());

        Assert.Null(provider.VerifyAndParseNotification(SignedNotification(NotificationPayload(amount))));
    }

    [Fact]
    public void Signed_notification_that_is_not_a_json_object_is_rejected()
    {
        var (provider, _) = Create(_ => throw new InvalidOperationException());

        Assert.Null(provider.VerifyAndParseNotification(Hs256Jwt.Sign("[1,2,3]", Secret)));
    }

    // ---- helpers ----

    private static readonly object SuccessPayload = new
    {
        webPaymentUrl = "https://sandbox-pgw-ui.2c2p.com/payment/4.3/#/token/kSAops9Zwhos8hSTSeLTUcCnr6",
        paymentToken = "kSAops9Zwhos8hSTSeLTUcCnr6",
        respCode = "0000",
        respDesc = "Success",
    };

    private static (TwoCTwoPPaymentProvider Provider, FakeHttpMessageHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHttpMessageHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        var options = Options.Create(new TwoCTwoPOptions { MerchantId = MerchantId, SecretKey = Secret, BaseUrl = BaseUrl });
        return (new TwoCTwoPPaymentProvider(http, options, NullLogger<TwoCTwoPPaymentProvider>.Instance), handler);
    }

    private static PaymentTokenRequest Request(PaymentIntent intent, string? customerToken = null) =>
        new("INV001", "Wireless Earbuds", 10.00m, "SGD",
            "https://shop.example.com/return", "https://api.example.com/notify", intent, customerToken);

    private static void AssertCommonFields(JsonElement payload)
    {
        Assert.Equal(MerchantId, payload.GetProperty("merchantID").GetString());
        Assert.Equal("INV001", payload.GetProperty("invoiceNo").GetString());
        Assert.Equal("Wireless Earbuds", payload.GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Number, payload.GetProperty("amount").ValueKind);
        Assert.Equal(10.00m, payload.GetProperty("amount").GetDecimal());
        Assert.Equal("SGD", payload.GetProperty("currencyCode").GetString());
        Assert.Equal("https://shop.example.com/return", payload.GetProperty("frontendReturnUrl").GetString());
        Assert.Equal("https://api.example.com/notify", payload.GetProperty("backendReturnUrl").GetString());
    }

    // The request body must be {"payload":"<JWT>"} and the JWT must verify with our secret.
    private static JsonElement DecodeRequestPayload(FakeHttpMessageHandler handler)
    {
        using var body = JsonDocument.Parse(handler.RequestBody!);
        var jwt = body.RootElement.GetProperty("payload").GetString()!;

        Assert.True(Hs256Jwt.TryVerify(jwt, Secret, out var payloadJson), "request JWT should verify");
        return JsonDocument.Parse(payloadJson).RootElement.Clone();
    }

    private static string[] StringArray(JsonElement payload, string name) =>
        payload.GetProperty(name).EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static HttpResponseMessage Signed(object payload, string secret = Secret) =>
        Json(new { payload = Hs256Jwt.Sign(JsonSerializer.Serialize(payload), secret) });

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
}
