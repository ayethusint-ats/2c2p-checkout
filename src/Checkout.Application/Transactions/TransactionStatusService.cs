using Checkout.Application.Abstractions;
using Checkout.Application.Common;
using Checkout.Application.Dtos;
using Checkout.Application.Payments;
using Checkout.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Checkout.Application.Transactions;

/// <summary>
/// Reads a payment's status. Saved-card payments are reconciled with a Payment Inquiry while Pending,
/// because their outcome may never reach us any other way (no browser comes back, and the notification
/// can't reach a local API). Hosted-page payments are left to the notification.
/// Never cached: the status is exactly what a client polls to see change.
/// </summary>
public sealed class TransactionStatusService(
    ITransactionRepository transactions,
    IPaymentProvider paymentProvider,
    IPaymentResultProcessor processor,
    ILogger<TransactionStatusService> logger) : ITransactionStatusService
{
    public async Task<TransactionStatusDto> GetAsync(string invoiceNo, CancellationToken ct)
    {
        var transaction = await transactions.FindByInvoiceNoAsync(invoiceNo, ct)
            ?? throw new NotFoundException("Transaction", invoiceNo);

        if (transaction.Status == TransactionStatus.Pending && transaction.CustomerCardId is not null)
            await ReconcileAsync(invoiceNo, ct);

        // Re-read after reconciling: the processor updates the row directly in the database.
        return await transactions.GetStatusAsync(invoiceNo, ct)
            ?? throw new NotFoundException("Transaction", invoiceNo);
    }

    private async Task ReconcileAsync(string invoiceNo, CancellationToken ct)
    {
        try
        {
            var result = await paymentProvider.InquirePaymentAsync(invoiceNo, ct);
            await processor.ApplyAsync(result, ct);
        }
        catch (PaymentProviderException ex)
        {
            // The stored status is still true as far as we know; report it and try again on the next read.
            logger.LogError(ex, "Payment inquiry failed for invoice {InvoiceNo}; returning the stored status", invoiceNo);
        }
    }
}
