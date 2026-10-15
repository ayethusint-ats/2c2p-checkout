using Checkout.Application.Abstractions;
using Checkout.Application.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Checkout.Infrastructure.Persistence.Repositories;

internal sealed class ProductRepository(CheckoutDbContext db) : IProductRepository
{
    public async Task<IReadOnlyList<ProductDto>> GetAllAsync(CancellationToken ct) =>
        await db.Products
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .Select(p => new ProductDto(p.Id, p.Name, p.Description, p.Price, p.Currency, p.PaymentType))
            .ToListAsync(ct);
}
