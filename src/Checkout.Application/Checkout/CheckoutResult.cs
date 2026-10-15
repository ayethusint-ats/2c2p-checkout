using Checkout.Domain.Enums;

namespace Checkout.Application.Checkout;

/// <param name="RedirectUrl">The 2C2P payment page; set only while the transaction is Pending.</param>
public sealed record CheckoutResult(string InvoiceNo, TransactionStatus Status, string? RedirectUrl);
