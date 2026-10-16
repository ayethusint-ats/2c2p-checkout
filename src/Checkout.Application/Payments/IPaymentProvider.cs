namespace Checkout.Application.Payments;

public interface IPaymentProvider
{
    /// <exception cref="PaymentProviderException">
    /// The provider could not be reached, timed out, returned a non-success HTTP status,
    /// or sent a response whose signature could not be verified.
    /// </exception>
    Task<PaymentTokenResult> CreatePaymentTokenAsync(PaymentTokenRequest request, CancellationToken ct);

    /// <summary>Charges a saved card using a payment token created with <see cref="PaymentIntent.SavedCard"/>.</summary>
    /// <exception cref="PaymentProviderException">
    /// Transport error, timeout, non-success HTTP status or bad signature: the charge may or may not have happened.
    /// </exception>
    Task<DoPaymentResult> DoPaymentWithSavedCardAsync(string paymentToken, string customerToken, CancellationToken ct);

    /// <summary>Asks the provider for the current result of a payment.</summary>
    /// <exception cref="PaymentProviderException">
    /// Transport error, timeout, non-success HTTP status, bad signature, or a result for another invoice.
    /// </exception>
    Task<PaymentResultInfo> InquirePaymentAsync(string invoiceNo, CancellationToken ct);

    /// <summary>
    /// Verifies and reads a payment result notification. Returns null if the signature is invalid,
    /// the payload is malformed, or it is for another merchant. Never throws on bad input.
    /// </summary>
    PaymentResultInfo? VerifyAndParseNotification(string payload);
}
