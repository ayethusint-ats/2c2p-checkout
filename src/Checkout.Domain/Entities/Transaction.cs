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
}
