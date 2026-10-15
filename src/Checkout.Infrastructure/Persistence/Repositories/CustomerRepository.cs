using Checkout.Application.Abstractions;
using Checkout.Application.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Checkout.Infrastructure.Persistence.Repositories;

internal sealed class CustomerRepository(CheckoutDbContext db) : ICustomerRepository
{
    public async Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken ct) =>
        await db.Customers
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => new CustomerDto(c.Id, c.FirstName, c.LastName, c.Email))
            .ToListAsync(ct);

    public Task<bool> ExistsAsync(int id, CancellationToken ct) =>
        db.Customers.AnyAsync(c => c.Id == id, ct);
}
