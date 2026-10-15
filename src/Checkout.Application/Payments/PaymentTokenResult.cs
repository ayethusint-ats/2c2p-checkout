namespace Checkout.Application.Payments;

/// <summary>
/// The provider's answer. A declined or rejected request is a normal result
/// (Succeeded = false with the provider's code), not an exception.
/// </summary>
public sealed record PaymentTokenResult(
    bool Succeeded,
    string? PaymentToken,
    string? RedirectUrl,
    string RespCode,
    string RespDesc);
