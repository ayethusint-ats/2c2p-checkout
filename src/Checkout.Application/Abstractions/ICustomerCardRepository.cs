using Checkout.Application.Dtos;

namespace Checkout.Application.Abstractions;

public interface ICustomerCardRepository
{
    Task<IReadOnlyList<SavedCardDto>> GetByCustomerIdAsync(int customerId, CancellationToken ct);
}
