namespace Checkout.Domain.Entities;

/// <summary>
/// A card saved at 2C2P. We keep only 2C2P's customer token and a masked
/// number for display; the full card number and CVV never reach us.
/// </summary>
public class CustomerCard
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public required string CustomerToken { get; set; }
    public required string MaskedCardNo { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }
}
