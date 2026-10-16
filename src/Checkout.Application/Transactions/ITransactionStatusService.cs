using Checkout.Application.Dtos;

namespace Checkout.Application.Transactions;

public interface ITransactionStatusService
{
    /// <summary>
    /// The current state of a payment. A Pending saved-card payment is first checked with the provider.
    /// </summary>
    /// <exception cref="Common.NotFoundException">No transaction has this invoice number.</exception>
    Task<TransactionStatusDto> GetAsync(string invoiceNo, CancellationToken ct);
}
