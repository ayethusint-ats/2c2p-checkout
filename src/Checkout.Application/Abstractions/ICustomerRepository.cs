using Checkout.Application.Dtos;
using Checkout.Domain.Entities;

namespace Checkout.Application.Abstractions;

public interface ICustomerRepository
{
    Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken ct);
    Task<bool> ExistsAsync(int id, CancellationToken ct);

    /// <summary>The entity itself (not cached), or null if it does not exist.</summary>
    Task<Customer?> FindAsync(int id, CancellationToken ct);
}
