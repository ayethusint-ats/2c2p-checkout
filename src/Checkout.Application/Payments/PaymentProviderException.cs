namespace Checkout.Application.Payments;

/// <summary>
/// The provider call itself failed (network, timeout, HTTP status, bad signature),
/// so its outcome is unknown or untrustworthy.
/// </summary>
public sealed class PaymentProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);
