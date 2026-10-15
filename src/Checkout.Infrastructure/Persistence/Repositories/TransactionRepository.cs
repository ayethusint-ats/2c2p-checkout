using Checkout.Application.Abstractions;
using Checkout.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Checkout.Infrastructure.Persistence.Repositories;

internal sealed class TransactionRepository(CheckoutDbContext db) : ITransactionRepository
{
    // Only used to replay a stored result, so it is not tracked.
    public Task<Transaction?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct) =>
        db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey, ct);

    // Tracked from here on, so later changes (AttachPaymentToken, MarkFailed) are saved as UPDATEs.
    public async Task AddAsync(Transaction transaction, CancellationToken ct) =>
        await db.Transactions.AddAsync(transaction, ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
