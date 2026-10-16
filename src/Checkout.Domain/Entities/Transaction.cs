using Checkout.Domain.Enums;

namespace Checkout.Domain.Entities;

public class Transaction
{
    public long Id { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Set only when the payment used a saved card.</summary>
    public int? CustomerCardId { get; set; }
    public CustomerCard? CustomerCard { get; set; }

    /// <summary>Our reference sent to 2C2P; unique per payment attempt.</summary>
    public required string InvoiceNo { get; set; }

    /// <summary>Client-supplied key so a retried request can't create a second payment.</summary>
    public required string IdempotencyKey { get; set; }

    /// <summary>Copied from the product at purchase time, so later price changes don't rewrite history.</summary>
    public decimal Amount { get; set; }
    public required string Currency { get; set; }

    public TransactionStatus Status { get; set; }

    /// <summary>The 2C2P paymentToken returned by the Payment Token API.</summary>
    public string? PaymentToken { get; set; }

    /// <summary>The hosted payment page URL for this attempt (it embeds the payment token).</summary>
    public string? RedirectUrl { get; set; }

    // Filled in from the 2C2P payment result.
    public string? PaymentChannel { get; set; }
    public string? MaskedCardNo { get; set; }
    public string? ProviderRef { get; set; }
    public string? RespCode { get; set; }
    public string? RespDesc { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC; null until the payment succeeds.</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>
    /// The provider created a payment token for this attempt; it stays Pending until paid.
    /// A saved-card payment has no page yet, so <paramref name="redirectUrl"/> may be null.
    /// </summary>
    public void AttachPaymentToken(string paymentToken, string? redirectUrl = null)
    {
        EnsurePending(nameof(AttachPaymentToken));
        PaymentToken = paymentToken;
        RedirectUrl = redirectUrl;
    }

    /// <summary>The customer must finish this payment at <paramref name="redirectUrl"/> (e.g. a 3-D Secure challenge).</summary>
    public void SetRedirectUrl(string redirectUrl)
    {
        EnsurePending(nameof(SetRedirectUrl));
        RedirectUrl = redirectUrl;
    }

    /// <summary>The attempt ended without payment, with the provider's (or our) reason.</summary>
    public void MarkFailed(string respCode, string respDesc)
    {
        EnsurePending(nameof(MarkFailed));
        Status = TransactionStatus.Failed;
        RespCode = respCode;
        RespDesc = respDesc;
    }

    // A finished payment (Succeeded or Failed) must never be rewritten, e.g. by a late or replayed message.
    private void EnsurePending(string operation)
    {
        if (Status != TransactionStatus.Pending)
            throw new InvalidOperationException(
                $"Cannot {operation} transaction {InvoiceNo}: status is {Status}, expected {TransactionStatus.Pending}.");
    }
}
