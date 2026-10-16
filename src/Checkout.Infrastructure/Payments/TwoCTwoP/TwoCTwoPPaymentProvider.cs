using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Checkout.Application.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Checkout.Infrastructure.Payments.TwoCTwoP;

internal sealed class TwoCTwoPPaymentProvider(
    HttpClient http,
    IOptions<TwoCTwoPOptions> options,
    ILogger<TwoCTwoPPaymentProvider> logger) : IPaymentProvider
{
    private const string SuccessCode = "0000";

    // camelCase names, nulls omitted (so e.g. NewPaymentMethod sends no paymentChannel at all),
    // decimals written as JSON numbers.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<PaymentTokenResult> CreatePaymentTokenAsync(PaymentTokenRequest request, CancellationToken ct)
    {
        var settings = options.Value;

        var payloadJson = JsonSerializer.Serialize(BuildPayload(request, settings.MerchantId), JsonOptions);
        var body = new JwtEnvelope(Hs256Jwt.Sign(payloadJson, settings.SecretKey));

        string responseBody;
        try
        {
            // Relative URI: resolved against BaseAddress (…/payment/4.3/) set at registration.
            using var response = await http.PostAsJsonAsync("paymentToken", body, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
                throw new PaymentProviderException(
                    $"2C2P paymentToken returned HTTP {(int)response.StatusCode} for invoice {request.InvoiceNo}.");

            responseBody = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentProviderException($"2C2P paymentToken request failed for invoice {request.InvoiceNo}.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout surfaces as TaskCanceledException; caller-initiated cancellation is rethrown as is.
            throw new PaymentProviderException($"2C2P paymentToken timed out for invoice {request.InvoiceNo}.", ex);
        }

        var result = ParseResponse(responseBody, settings.SecretKey, request.InvoiceNo);

        // Only identifiers and the outcome code: never the secret, the JWTs or the payment token.
        logger.LogInformation(
            "2C2P paymentToken for invoice {InvoiceNo} ({Intent}): respCode {RespCode}",
            request.InvoiceNo, request.Intent, result.RespCode);

        return result;
    }

    public PaymentResultInfo? VerifyAndParseNotification(string payload)
    {
        var settings = options.Value;

        // Verify first: nothing in the payload is read or trusted until the signature checks out.
        if (string.IsNullOrEmpty(payload) || !Hs256Jwt.TryVerify(payload, settings.SecretKey, out var payloadJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var merchantId = GetString(root, "merchantID");
            var invoiceNo = GetString(root, "invoiceNo");
            var currencyCode = GetString(root, "currencyCode");
            var respCode = GetString(root, "respCode");

            if (string.IsNullOrEmpty(merchantId) || string.IsNullOrEmpty(invoiceNo)
                || string.IsNullOrEmpty(currencyCode) || string.IsNullOrEmpty(respCode)
                || !TryGetAmount(root, out var amount))
            {
                return null;
            }

            // Correctly signed, but for another merchant account: not ours to apply.
            if (!string.Equals(merchantId, settings.MerchantId, StringComparison.Ordinal))
                return null;

            return new PaymentResultInfo(
                merchantId,
                invoiceNo,
                amount,
                currencyCode,
                respCode,
                GetString(root, "respDesc") ?? string.Empty,
                GetString(root, "channelCode"),
                GetString(root, "accountNo"),
                GetString(root, "tranRef"),
                GetString(root, "customerToken"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // 2C2P may send the amount as a number (230.87) or a string ("230.87"). Invariant culture,
    // and only digits with an optional decimal point: no sign, no thousands separators.
    private static bool TryGetAmount(JsonElement root, out decimal amount)
    {
        amount = 0;
        if (!root.TryGetProperty("amount", out var value))
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDecimal(out amount),
            JsonValueKind.String => decimal.TryParse(value.GetString(), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out amount),
            _ => false,
        };
    }

    // Maps our provider-neutral intent onto 2C2P's request fields.
    private static PaymentTokenRequestPayload BuildPayload(PaymentTokenRequest request, string merchantId)
    {
        var payload = new PaymentTokenRequestPayload
        {
            MerchantID = merchantId,
            InvoiceNo = request.InvoiceNo,
            Description = request.Description,
            Amount = request.Amount,
            CurrencyCode = request.CurrencyCode,
            FrontendReturnUrl = request.FrontendReturnUrl,
            BackendReturnUrl = request.BackendReturnUrl,
        };

        return request.Intent switch
        {
            // Let 2C2P show every payment method enabled for the merchant.
            PaymentIntent.NewPaymentMethod => payload,

            // Card only, and ask 2C2P to tokenize it so it can be saved.
            PaymentIntent.NewCardAndSave => payload with
            {
                PaymentChannel = ["CC"],
                Tokenize = true,
            },

            // Charge the saved token only, without asking for 3-D Secure again.
            PaymentIntent.SavedCard => payload with
            {
                PaymentChannel = ["CC"],
                CustomerToken = [request.CustomerToken
                    ?? throw new ArgumentException("CustomerToken is required for the SavedCard intent.", nameof(request))],
                CustomerTokenOnly = true,
                Request3DS = "N",
            },

            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Intent, "Unknown payment intent."),
        };
    }

    private static PaymentTokenResult ParseResponse(string responseBody, string secret, string invoiceNo)
    {
        ResponseBody? body;
        try
        {
            body = JsonSerializer.Deserialize<ResponseBody>(responseBody, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new PaymentProviderException($"2C2P paymentToken response for invoice {invoiceNo} is not valid JSON.", ex);
        }

        if (body?.Payload is null)
        {
            // Some request errors come back unsigned. They can only ever become a failure:
            // a success without a verified signature is never trusted.
            if (body?.RespCode is { } code && code != SuccessCode)
                return new PaymentTokenResult(false, null, null, code, body.RespDesc ?? string.Empty);

            throw new PaymentProviderException($"2C2P paymentToken response for invoice {invoiceNo} has no signed payload.");
        }

        // Verify first; nothing inside the payload is read until the signature checks out.
        if (!Hs256Jwt.TryVerify(body.Payload, secret, out var payloadJson))
            throw new PaymentProviderException($"2C2P paymentToken response for invoice {invoiceNo} failed signature verification.");

        PaymentTokenResponsePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PaymentTokenResponsePayload>(payloadJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new PaymentProviderException($"2C2P paymentToken payload for invoice {invoiceNo} is not valid JSON.", ex);
        }

        if (payload?.RespCode is null)
            throw new PaymentProviderException($"2C2P paymentToken payload for invoice {invoiceNo} has no respCode.");

        if (payload.RespCode != SuccessCode)
            return new PaymentTokenResult(false, null, null, payload.RespCode, payload.RespDesc ?? string.Empty);

        if (string.IsNullOrEmpty(payload.PaymentToken) || string.IsNullOrEmpty(payload.WebPaymentUrl))
            throw new PaymentProviderException($"2C2P paymentToken succeeded for invoice {invoiceNo} but returned no token or URL.");

        return new PaymentTokenResult(true, payload.PaymentToken, payload.WebPaymentUrl, payload.RespCode, payload.RespDesc ?? string.Empty);
    }
}
