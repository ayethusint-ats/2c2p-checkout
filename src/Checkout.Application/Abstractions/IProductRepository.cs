using Checkout.Application.Dtos;
using Checkout.Domain.Entities;

namespace Checkout.Application.Abstractions;

public interface IProductRepository
{
    Task<IReadOnlyList<ProductDto>> GetAllAsync(CancellationToken ct);

    /// <summary>The entity itself (not cached, so the price is current), or null if it does not exist.</summary>
    Task<Product?> FindAsync(int id, CancellationToken ct);
}
