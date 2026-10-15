namespace Checkout.Application.Payments;

public interface IPaymentProvider
{
    /// <exception cref="PaymentProviderException">
    /// The provider could not be reached, timed out, returned a non-success HTTP status,
    /// or sent a response whose signature could not be verified.
    /// </exception>
    Task<PaymentTokenResult> CreatePaymentTokenAsync(PaymentTokenRequest request, CancellationToken ct);
}
