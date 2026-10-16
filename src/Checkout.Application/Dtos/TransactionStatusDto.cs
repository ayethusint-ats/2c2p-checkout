namespace Checkout.Application.Dtos;

/// <summary>
/// What a client may see about a payment. Deliberately omits the payment token, the idempotency key,
/// the redirect URL and any customer token: each of them could be used to act on the payment.
/// </summary>
public sealed record TransactionStatusDto(
    string InvoiceNo,
    string Status,
    int CustomerId,
    int ProductId,
    string ProductName,
    decimal Amount,
    string CurrencyCode,
    string? PaymentChannel,
    string? MaskedCardNo,
    string? RespDesc,
    DateTime? PaidAt,
    DateTime CreatedAt);
