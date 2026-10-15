using Checkout.Application.Abstractions;
using Checkout.Application.Common;
using Checkout.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Checkout.Infrastructure.Persistence.Repositories;

internal sealed class TransactionRepository(CheckoutDbContext db) : ITransactionRepository
{
    private const string IdempotencyKeyIndex = "ux_transaction_idempotency_key";

    // Untracked, so every call reads the current row from MySQL instead of EF's cached copy.
    // That matters when polling for another request's result.
    public Task<Transaction?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct) =>
        db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey, ct);

    // Tracked from here on, so later changes (AttachPaymentToken, MarkFailed) are saved as UPDATEs.
    public async Task AddAsync(Transaction transaction, CancellationToken ct) =>
        await db.Transactions.AddAsync(transaction, ct);

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDuplicateIdempotencyKey(ex))
        {
            // Stop tracking the rejected row, so a later SaveChanges on this context doesn't retry the INSERT.
            foreach (var entry in ex.Entries)
                entry.State = EntityState.Detached;

            var key = ex.Entries.Select(e => e.Entity).OfType<Transaction>().FirstOrDefault()?.IdempotencyKey;
            throw new DuplicateIdempotencyKeyException(key ?? "(unknown)", ex);
        }
    }

    // Error 1062 alone means "some unique index"; the index name tells us it is this one
    // (and not, say, a colliding invoice_no). MySQL 8: "Duplicate entry '…' for key 'transaction.ux_…'".
    private static bool IsDuplicateIdempotencyKey(DbUpdateException ex) =>
        ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry } mysql
        && mysql.Message.Contains(IdempotencyKeyIndex, StringComparison.Ordinal);
}
