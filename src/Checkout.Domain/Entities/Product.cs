using Checkout.Domain.Enums;

namespace Checkout.Domain.Entities;

public class Product
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public decimal Price { get; set; }

    /// <summary>ISO 4217 code, e.g. "SGD".</summary>
    public required string Currency { get; set; }

    public PaymentType PaymentType { get; set; }
}
