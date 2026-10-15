using Checkout.Application.Dtos;

namespace Checkout.Application.Abstractions;

public interface ICustomerRepository
{
    Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken ct);
    Task<bool> ExistsAsync(int id, CancellationToken ct);
}
