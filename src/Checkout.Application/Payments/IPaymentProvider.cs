namespace Checkout.Application.Payments;

public interface IPaymentProvider
{
    /// <exception cref="PaymentProviderException">
    /// The provider could not be reached, timed out, returned a non-success HTTP status,
    /// or sent a response whose signature could not be verified.
    /// </exception>
    Task<PaymentTokenResult> CreatePaymentTokenAsync(PaymentTokenRequest request, CancellationToken ct);

    /// <summary>
    /// Verifies and reads a payment result notification. Returns null if the signature is invalid,
    /// the payload is malformed, or it is for another merchant. Never throws on bad input.
    /// </summary>
    PaymentResultInfo? VerifyAndParseNotification(string payload);
}
