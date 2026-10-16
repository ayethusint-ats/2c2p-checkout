using Checkout.Application.Abstractions;
using Checkout.Domain.Entities;
using Checkout.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Checkout.Application.Payments;

/// <summary>
/// Applies a verified payment result. Safe to run any number of times, in any order, concurrently:
/// the status changes only through a conditional UPDATE from Pending, and the card is saved
/// based on the final status (insert-if-absent), not on whether this call changed anything.
/// </summary>
public sealed class PaymentResultProcessor(
    ITransactionRepository transactions,
    ICustomerCardRepository cards,
    TimeProvider timeProvider,
    ILogger<PaymentResultProcessor> logger) : IPaymentResultProcessor
{
    private const string SuccessCode = "0000";

    // Not an outcome of the payment: 2001 in progress, 2002 not found (yet), 2003 inquiry failed.
    private static readonly HashSet<string> NotFinalCodes = ["2001", "2002", "2003"];

    // Column sizes from TransactionConfiguration / CustomerCardConfiguration.
    private const int PaymentChannelMax = 20;
    private const int MaskedCardNoMax = 30;
    private const int ProviderRefMax = 50;
    private const int RespCodeMax = 10;
    private const int RespDescMax = 100;
    private const int CustomerTokenMax = 100;

    public async Task<PaymentResultOutcome> ApplyAsync(PaymentResultInfo result, CancellationToken ct)
    {
        var outcome = await ApplyCoreAsync(result, ct);

        // One line per result. Never the JWT, the payload or the customer token.
        logger.LogInformation("Payment result for invoice {InvoiceNo}: respCode {RespCode}, outcome {Outcome}",
            result.InvoiceNo, result.RespCode, outcome);

        return outcome;
    }

    private async Task<PaymentResultOutcome> ApplyCoreAsync(PaymentResultInfo result, CancellationToken ct)
    {
        // 0. "No answer yet" must never be read as a failure: the customer may still be charged.
        if (NotFinalCodes.Contains(result.RespCode))
            return PaymentResultOutcome.NotFinal;

        var succeeded = result.RespCode == SuccessCode;

        // 1. Which payment is this about?
        var transaction = await transactions.FindByInvoiceNoAsync(result.InvoiceNo, ct);
        if (transaction is null)
            return PaymentResultOutcome.UnknownInvoice;

        // 2. The result must be for exactly what we asked the customer to pay.
        if (!MatchesTransaction(result, transaction, requireAmountAndCurrency: succeeded))
        {
            logger.LogError(
                "Payment result for invoice {InvoiceNo} does not match the transaction: " +
                "received {ReceivedAmount} {ReceivedCurrency}, expected {ExpectedAmount} {ExpectedCurrency}. Not applied.",
                result.InvoiceNo, result.Amount, result.CurrencyCode, transaction.Amount, transaction.Currency);
            return PaymentResultOutcome.AmountOrCurrencyMismatch;
        }

        var respCode = Truncate(result.RespCode, RespCodeMax)!;
        var respDesc = Truncate(result.RespDesc, RespDescMax)!;

        // 3. Move Pending -> final in one conditional UPDATE. 0 rows = someone already finalised it.
        var rows = succeeded
            ? await transactions.MarkSucceededIfPendingAsync(
                transaction.Id,
                Truncate(result.ChannelCode, PaymentChannelMax),
                Truncate(result.AccountNo, MaskedCardNoMax),
                Truncate(result.TranRef, ProviderRefMax),
                respCode,
                respDesc,
                timeProvider.GetUtcNow().UtcDateTime,
                ct)
            : await transactions.MarkFailedIfPendingAsync(transaction.Id, respCode, respDesc, ct);

        var outcome = rows > 0 ? PaymentResultOutcome.Applied : PaymentResultOutcome.AlreadyFinal;

        // 4. Save the card from the final state, so a retry can complete a card save that failed earlier.
        if (succeeded && !string.IsNullOrEmpty(result.CustomerToken))
            await SaveCardIfSucceededAsync(result, ct);

        return outcome;
    }

    private async Task SaveCardIfSucceededAsync(PaymentResultInfo result, CancellationToken ct)
    {
        // Re-read: the decision depends on the stored status, not on what this result says.
        var current = await transactions.FindByInvoiceNoAsync(result.InvoiceNo, ct);
        if (current is null || current.Status != TransactionStatus.Succeeded)
            return;

        // A cut-off token would be useless (and would never match 2C2P's), so don't store one.
        if (result.CustomerToken!.Length > CustomerTokenMax)
        {
            logger.LogWarning("Customer token for invoice {InvoiceNo} exceeds {Max} characters; card not saved",
                result.InvoiceNo, CustomerTokenMax);
            return;
        }

        // The card belongs to the customer on OUR transaction, never to anyone named in the payload.
        var inserted = await cards.AddIfNotExistsAsync(
            current.CustomerId,
            result.CustomerToken,
            Truncate(result.AccountNo, MaskedCardNoMax) ?? string.Empty,
            timeProvider.GetUtcNow().UtcDateTime,
            ct);

        if (inserted)
            logger.LogInformation("Saved card for customer {CustomerId} from invoice {InvoiceNo}",
                current.CustomerId, result.InvoiceNo);
    }

    // A success must prove what was paid, so missing values count as a mismatch. A failure moves
    // no money, so missing values are accepted, but values that are present must still match.
    private static bool MatchesTransaction(PaymentResultInfo result, Transaction transaction, bool requireAmountAndCurrency)
    {
        var amountOk = result.Amount is { } amount
            ? amount == transaction.Amount
            : !requireAmountAndCurrency;

        var currencyOk = !string.IsNullOrEmpty(result.CurrencyCode)
            ? string.Equals(result.CurrencyCode, transaction.Currency, StringComparison.OrdinalIgnoreCase)
            : !requireAmountAndCurrency;

        return amountOk && currencyOk;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
