using Checkout.Domain.Entities;

namespace Checkout.Application.Abstractions;

public interface ITransactionRepository
{
    /// <summary>Read-only lookup used for replaying a request; the result is not tracked for updates.</summary>
    Task<Transaction?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct);

    Task AddAsync(Transaction transaction, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);

    /// <summary>Untracked, so it always reflects the current row.</summary>
    Task<Transaction?> FindByInvoiceNoAsync(string invoiceNo, CancellationToken ct);

    /// <summary>
    /// One conditional UPDATE (… WHERE id = @id AND status = 'Pending'), without loading the row.
    /// </summary>
    /// <returns>Rows affected: 1 if it was Pending, 0 if it was already final (or missing).</returns>
    Task<int> MarkSucceededIfPendingAsync(long id, string? paymentChannel, string? maskedCardNo,
        string? providerRef, string respCode, string respDesc, DateTime paidAtUtc, CancellationToken ct);

    /// <inheritdoc cref="MarkSucceededIfPendingAsync"/>
    Task<int> MarkFailedIfPendingAsync(long id, string respCode, string respDesc, CancellationToken ct);
}
