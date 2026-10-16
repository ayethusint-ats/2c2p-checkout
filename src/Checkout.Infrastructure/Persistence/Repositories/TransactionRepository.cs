using Checkout.Application.Abstractions;
using Checkout.Application.Common;
using Checkout.Domain.Entities;
using Checkout.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Checkout.Infrastructure.Persistence.Repositories;

internal sealed class TransactionRepository(CheckoutDbContext db) : ITransactionRepository
{
    private const string IdempotencyKeyIndex = "ux_transaction_idempotency_key";

    // Untracked, so every call reads the current row from MySQL instead of EF's cached copy.
    // That matters when polling for another request's result.
    public Task<Transaction?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct) =>
        db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey, ct);

    public Task<Transaction?> FindByInvoiceNoAsync(string invoiceNo, CancellationToken ct) =>
        db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.InvoiceNo == invoiceNo, ct);

    // Tracked from here on, so later changes (AttachPaymentToken, MarkFailed) are saved as UPDATEs.
    public async Task AddAsync(Transaction transaction, CancellationToken ct) =>
        await db.Transactions.AddAsync(transaction, ct);

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (MySqlErrors.IsDuplicateKey(ex, IdempotencyKeyIndex))
        {
            // Stop tracking the rejected row, so a later SaveChanges on this context doesn't retry the INSERT.
            foreach (var entry in ex.Entries)
                entry.State = EntityState.Detached;

            var key = ex.Entries.Select(e => e.Entity).OfType<Transaction>().FirstOrDefault()?.IdempotencyKey;
            throw new DuplicateIdempotencyKeyException(key ?? "(unknown)", ex);
        }
    }

    // The status check is part of the UPDATE itself, so MySQL decides atomically:
    // of two concurrent results, exactly one sees status = 'Pending' and changes the row.
    public Task<int> MarkSucceededIfPendingAsync(long id, string? paymentChannel, string? maskedCardNo,
        string? providerRef, string respCode, string respDesc, DateTime paidAtUtc, CancellationToken ct) =>
        db.Transactions
            .Where(t => t.Id == id && t.Status == TransactionStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TransactionStatus.Succeeded)
                .SetProperty(t => t.PaymentChannel, paymentChannel)
                .SetProperty(t => t.MaskedCardNo, maskedCardNo)
                .SetProperty(t => t.ProviderRef, providerRef)
                .SetProperty(t => t.RespCode, respCode)
                .SetProperty(t => t.RespDesc, respDesc)
                .SetProperty(t => t.PaidAt, paidAtUtc), ct);

    public Task<int> MarkFailedIfPendingAsync(long id, string respCode, string respDesc, CancellationToken ct) =>
        db.Transactions
            .Where(t => t.Id == id && t.Status == TransactionStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TransactionStatus.Failed)
                .SetProperty(t => t.RespCode, respCode)
                .SetProperty(t => t.RespDesc, respDesc), ct);
}
