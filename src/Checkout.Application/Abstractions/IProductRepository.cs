using Checkout.Application.Dtos;

namespace Checkout.Application.Abstractions;

public interface IProductRepository
{
    Task<IReadOnlyList<ProductDto>> GetAllAsync(CancellationToken ct);
}
