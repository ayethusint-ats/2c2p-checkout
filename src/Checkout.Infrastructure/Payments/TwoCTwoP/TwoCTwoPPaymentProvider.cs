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
    private const string Locale = "en";

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

        var responseBody = await PostAsync("paymentToken", body, $"paymentToken for invoice {request.InvoiceNo}", ct);

        var result = ParseResponse(responseBody, settings.SecretKey, request.InvoiceNo);

        // Only identifiers and the outcome code: never the secret, the JWTs or the payment token.
        logger.LogInformation(
            "2C2P paymentToken for invoice {InvoiceNo} ({Intent}): respCode {RespCode}",
            request.InvoiceNo, request.Intent, result.RespCode);

        return result;
    }

    public async Task<DoPaymentResult> DoPaymentWithSavedCardAsync(
        string paymentToken, string customerToken, CancellationToken ct)
    {
        // No responseReturnUrl: we act on the response ourselves instead of having 2C2P redirect.
        var body = new DoPaymentRequest(
            paymentToken,
            Locale,
            new DoPaymentMethod(new DoPaymentCode("CC"), new DoPaymentData(customerToken)));

        var responseBody = await PostAsync("payment", body, "payment (Do Payment)", ct);
        var response = ParseDoPaymentResponse(responseBody);

        if (string.IsNullOrEmpty(response.RespCode))
            throw new PaymentProviderException("2C2P Do Payment response has no respCode.");

        var respDesc = response.RespDesc ?? string.Empty;
        var result = response.RespCode switch
        {
            // The customer must complete a challenge (e.g. 3-D Secure) in the browser.
            "1000" or "1001" => new DoPaymentResult(
                DoPaymentNextStep.RedirectBrowser, response.RespCode, respDesc, RequireHttpsUrl(response.Data)),

            // Still processing: the result comes later.
            "2001" => new DoPaymentResult(DoPaymentNextStep.WaitForResult, response.RespCode, respDesc, null),

            // 2000 ("completed, inquire for the result") and every other code: Do Payment's own code
            // is not a trustworthy payment outcome, so the inquiry decides.
            _ => new DoPaymentResult(DoPaymentNextStep.InquireResult, response.RespCode, respDesc, null),
        };

        // Never the payment token, customer token or body.
        logger.LogInformation("2C2P Do Payment: respCode {RespCode}, next step {NextStep}", result.RespCode, result.NextStep);
        return result;
    }

    public async Task<PaymentResultInfo> InquirePaymentAsync(string invoiceNo, CancellationToken ct)
    {
        var settings = options.Value;

        var payloadJson = JsonSerializer.Serialize(new PaymentInquiryRequestPayload(settings.MerchantId, invoiceNo, Locale), JsonOptions);
        var body = new JwtEnvelope(Hs256Jwt.Sign(payloadJson, settings.SecretKey));

        var responseBody = await PostAsync("paymentInquiry", body, $"paymentInquiry for invoice {invoiceNo}", ct);

        ResponseBody? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ResponseBody>(responseBody, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new PaymentProviderException($"2C2P paymentInquiry response for invoice {invoiceNo} is not valid JSON.", ex);
        }

        // The inquiry decides whether money moved, so an unsigned answer is never used, not even a failure.
        if (envelope?.Payload is null || !Hs256Jwt.TryVerify(envelope.Payload, settings.SecretKey, out var resultJson))
            throw new PaymentProviderException($"2C2P paymentInquiry response for invoice {invoiceNo} failed signature verification.");

        PaymentResultInfo? result;
        try
        {
            using var document = JsonDocument.Parse(resultJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new PaymentProviderException($"2C2P paymentInquiry payload for invoice {invoiceNo} is not a JSON object.");

            // Answers about a different payment must not be applied to this one.
            var returnedInvoiceNo = GetString(root, "invoiceNo");
            if (!string.IsNullOrEmpty(returnedInvoiceNo) && !string.Equals(returnedInvoiceNo, invoiceNo, StringComparison.Ordinal))
                throw new PaymentProviderException(
                    $"2C2P paymentInquiry for invoice {invoiceNo} returned a result for invoice {returnedInvoiceNo}.");

            // Not-found or in-progress answers may carry only respCode and respDesc.
            result = MapResult(root, GetString(root, "merchantID") ?? settings.MerchantId, invoiceNo);
        }
        catch (JsonException ex)
        {
            throw new PaymentProviderException($"2C2P paymentInquiry payload for invoice {invoiceNo} is not valid JSON.", ex);
        }

        if (result is null)
            throw new PaymentProviderException($"2C2P paymentInquiry payload for invoice {invoiceNo} has no respCode or an invalid amount.");

        logger.LogInformation("2C2P paymentInquiry for invoice {InvoiceNo}: respCode {RespCode}", invoiceNo, result.RespCode);
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
            if (string.IsNullOrEmpty(merchantId) || string.IsNullOrEmpty(invoiceNo))
                return null;

            // Correctly signed, but for another merchant account: not ours to apply.
            if (!string.Equals(merchantId, settings.MerchantId, StringComparison.Ordinal))
                return null;

            return MapResult(root, merchantId, invoiceNo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// Shared by the notification and the inquiry. Only respCode is required; amount, currency and the
    /// card details may be missing (the processor decides what a missing value means). An amount that
    /// is present but unreadable makes the whole result invalid (null).
    /// </summary>
    private static PaymentResultInfo? MapResult(JsonElement root, string merchantId, string invoiceNo)
    {
        var respCode = GetString(root, "respCode");
        if (string.IsNullOrEmpty(respCode)
            || !TryGetAmount(root, "amount", out var amount)
            || !TryGetAmount(root, "fxAmount", out var fxAmount))
            return null;

        var currencyCode = GetString(root, "currencyCode");
        var fxCurrencyCode = GetString(root, "fxCurrencyCode");

        return new PaymentResultInfo(
            merchantId,
            invoiceNo,
            amount,
            string.IsNullOrEmpty(currencyCode) ? null : currencyCode,
            respCode,
            GetString(root, "respDesc") ?? string.Empty,
            GetString(root, "channelCode"),
            GetString(root, "accountNo"),
            GetString(root, "tranRef"),
            GetString(root, "customerToken"),
            fxAmount,
            string.IsNullOrEmpty(fxCurrencyCode) ? null : fxCurrencyCode);
    }

    // 2C2P may send the amount as a number (230.87) or a string ("230.87"). Invariant culture,
    // and only digits with an optional decimal point: no sign, no thousands separators.
    // A missing (or null) amount is valid and gives null; a present but unreadable one is not.
    private static bool TryGetAmount(JsonElement root, string name, out decimal? amount)
    {
        amount = null;
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return true;

        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetDecimal(out var number):
                amount = number;
                return true;
            case JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var text):
                amount = text;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Posts JSON to a path under BaseUrl and returns the body. Transport problems become PaymentProviderException.</summary>
    /// <param name="description">For error messages only; must not contain tokens.</param>
    private async Task<string> PostAsync<T>(string path, T body, string description, CancellationToken ct)
    {
        try
        {
            // Relative URI: resolved against BaseAddress (…/payment/4.3/) set at registration.
            using var response = await http.PostAsJsonAsync(path, body, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
                throw new PaymentProviderException($"2C2P {description} returned HTTP {(int)response.StatusCode}.");

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentProviderException($"2C2P {description} request failed.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout surfaces as TaskCanceledException; caller-initiated cancellation is rethrown as is.
            throw new PaymentProviderException($"2C2P {description} timed out.", ex);
        }
    }

    // The response may be plain JSON or {"payload":"<JWT>"}. A payload is verified before anything
    // in it is read; a payload that fails verification is an error, never a fallback to the plain body.
    private DoPaymentResponse ParseDoPaymentResponse(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new PaymentProviderException("2C2P Do Payment response is not a JSON object.");

            if (!root.TryGetProperty("payload", out var payload))
                return ReadDoPaymentResponse(root);

            if (payload.ValueKind != JsonValueKind.String
                || !Hs256Jwt.TryVerify(payload.GetString()!, options.Value.SecretKey, out var payloadJson))
                throw new PaymentProviderException("2C2P Do Payment response failed signature verification.");

            using var decoded = JsonDocument.Parse(payloadJson);
            if (decoded.RootElement.ValueKind != JsonValueKind.Object)
                throw new PaymentProviderException("2C2P Do Payment payload is not a JSON object.");

            return ReadDoPaymentResponse(decoded.RootElement);
        }
        catch (JsonException ex)
        {
            throw new PaymentProviderException("2C2P Do Payment response is not valid JSON.", ex);
        }
    }

    // Read field by field rather than deserialized: "data" is only meaningful (a URL) for redirect codes,
    // and an unexpected shape there must not turn a normal response into an error.
    private static DoPaymentResponse ReadDoPaymentResponse(JsonElement root) =>
        new(GetString(root, "respCode"), GetString(root, "respDesc"), GetString(root, "data"));

    // The browser will be sent here, so only an absolute https URL is accepted.
    private static string RequireHttpsUrl(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            return url!;

        throw new PaymentProviderException("2C2P Do Payment asked for a redirect without an absolute https URL.");
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
