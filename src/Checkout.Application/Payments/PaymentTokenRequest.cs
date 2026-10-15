namespace Checkout.Application.Payments;

/// <param name="CustomerToken">Required when <paramref name="Intent"/> is <see cref="PaymentIntent.SavedCard"/>.</param>
public sealed record PaymentTokenRequest(
    string InvoiceNo,
    string Description,
    decimal Amount,
    string CurrencyCode,
    string FrontendReturnUrl,
    string BackendReturnUrl,
    PaymentIntent Intent,
    string? CustomerToken = null);
