using Checkout.Domain.Entities;

namespace Checkout.Application.Abstractions;

public interface ITransactionRepository
{
    /// <summary>Read-only lookup used for replaying a request; the result is not tracked for updates.</summary>
    Task<Transaction?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct);

    Task AddAsync(Transaction transaction, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
