using System.Net;
using System.Security.Cryptography;
using Checkout.Application.Abstractions;
using Checkout.Application.Common;
using Checkout.Application.Payments;
using Checkout.Domain.Entities;
using Checkout.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Checkout.Application.Checkout;

/// <summary>
/// Creates at most one transaction and one 2C2P payment page per idempotency key.
/// Guards, in order: replay of a stored row, a Redis lock per key, and finally the
/// MySQL unique index on idempotency_key (which also covers Redis being down).
/// </summary>
public sealed class CheckoutService(
    ICustomerRepository customers,
    IProductRepository products,
    ITransactionRepository transactions,
    IPaymentProvider paymentProvider,
    IDistributedLock distributedLock,
    TimeProvider timeProvider,
    IOptions<AppOptions> appOptions,
    IOptions<CheckoutOptions> checkoutOptions,
    ILogger<CheckoutService> logger) : ICheckoutService
{
    private const int MaxDescriptionLength = 250;

    public async Task<CheckoutResult> CheckoutAsync(CheckoutCommand command, CancellationToken ct)
    {
        var key = command.IdempotencyKey;

        // a. Fast path: this key was already used.
        var existing = await transactions.FindByIdempotencyKeyAsync(key, ct);
        if (existing is not null)
            return await ResolveAsync(existing, command, ct);

        // b. One request per key does the work; the others wait for its result.
        ILockHandle? lockHandle;
        try
        {
            lockHandle = await distributedLock.TryAcquireAsync(
                LockKey(key), TimeSpan.FromSeconds(checkoutOptions.Value.LockTtlSeconds), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The lock only avoids needless work; the unique index still prevents duplicates.
            logger.LogWarning(ex, "Redis unavailable, continuing without lock for idempotency key {IdempotencyKey}", key);
            return await CheckoutUnderLockAsync(command, lockHandle: null, ct);
        }

        if (lockHandle is null)
        {
            logger.LogInformation("Checkout lock busy for idempotency key {IdempotencyKey}; waiting for its result", key);
            return await WaitForResultAsync(command, ct);
        }

        logger.LogInformation("Checkout lock acquired for idempotency key {IdempotencyKey}", key);
        return await CheckoutUnderLockAsync(command, lockHandle, ct);
    }

    // c. Runs with the lock held, or with no lock when Redis is down.
    private async Task<CheckoutResult> CheckoutUnderLockAsync(
        CheckoutCommand command, ILockHandle? lockHandle, CancellationToken ct)
    {
        try
        {
            // Another request may have finished between the first check and acquiring the lock.
            var existing = await transactions.FindByIdempotencyKeyAsync(command.IdempotencyKey, ct);
            if (existing is not null)
                return await ResolveAsync(existing, command, ct);

            _ = await customers.FindAsync(command.CustomerId, ct)
                ?? throw new NotFoundException("Customer", command.CustomerId);
            var product = await products.FindAsync(command.ProductId, ct)
                ?? throw new NotFoundException("Product", command.ProductId);

            // Saved-card payments come in Step 8.
            if (command.CustomerCardId is not null)
                throw new FeatureNotImplementedException("Paying with a saved card is not available yet.");

            // Record the attempt before talking to 2C2P. Amount and currency come from the product,
            // never the request. Only FK ids are set: the loaded entities are untracked, and
            // attaching them would make EF try to insert them again.
            var transaction = new Transaction
            {
                CustomerId = command.CustomerId,
                ProductId = product.Id,
                InvoiceNo = NewInvoiceNo(),
                IdempotencyKey = command.IdempotencyKey,
                Amount = product.Price,
                Currency = product.Currency,
                Status = TransactionStatus.Pending,
                CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            };
            await transactions.AddAsync(transaction, ct);

            try
            {
                await transactions.SaveChangesAsync(ct);
            }
            catch (DuplicateIdempotencyKeyException)
            {
                // Final guard: another request (no lock, or an expired one) saved this key first.
                logger.LogInformation(
                    "Duplicate idempotency key {IdempotencyKey} rejected by the database; waiting for the winner's result",
                    command.IdempotencyKey);
                return await WaitForResultAsync(command, ct);
            }

            // From here the row exists: always finish, even if the client disconnects,
            // so it can't be left Pending without a token.
            return await RequestPaymentPageAsync(transaction, product);
        }
        finally
        {
            if (lockHandle is not null)
                await lockHandle.DisposeAsync();
        }
    }

    private async Task<CheckoutResult> RequestPaymentPageAsync(Transaction transaction, Product product)
    {
        var request = BuildPaymentTokenRequest(transaction, product);

        PaymentTokenResult result;
        try
        {
            // No DB transaction is open during this call.
            result = await paymentProvider.CreatePaymentTokenAsync(request, CancellationToken.None);
        }
        catch (PaymentProviderException ex)
        {
            logger.LogError(ex, "Payment provider unavailable for invoice {InvoiceNo}", transaction.InvoiceNo);
            transaction.MarkFailed("ERROR", "Payment provider unavailable");
            await transactions.SaveChangesAsync(CancellationToken.None);
            return ToResult(transaction);
        }

        if (result.Succeeded)
            transaction.AttachPaymentToken(result.PaymentToken!, result.RedirectUrl!);
        else
            transaction.MarkFailed(result.RespCode, result.RespDesc);

        await transactions.SaveChangesAsync(CancellationToken.None);

        logger.LogInformation("Checkout for invoice {InvoiceNo}: {Status} (respCode {RespCode})",
            transaction.InvoiceNo, transaction.Status, result.RespCode);

        return ToResult(transaction);
    }

    // d. A row already exists for this key.
    private async Task<CheckoutResult> ResolveAsync(Transaction existing, CheckoutCommand command, CancellationToken ct)
    {
        if (TryGetCompletedResult(existing, command) is { } result)
            return result;

        // Pending without a payment page: someone is still working on it (or it is stuck).
        return await WaitForResultAsync(command, ct);
    }

    // e. Waits for the row, never for the lock, and never calls the provider itself.
    // A Pending row with no redirect URL that never completes is stuck; retrying it here could
    // create a second payment page for the same invoice, so the client gets 409 and must use a new key.
    private async Task<CheckoutResult> WaitForResultAsync(CheckoutCommand command, CancellationToken ct)
    {
        var options = checkoutOptions.Value;
        var timeout = TimeSpan.FromSeconds(options.WaitTimeoutSeconds);
        var pollInterval = TimeSpan.FromMilliseconds(options.PollIntervalMilliseconds);

        // Monotonic clock: unaffected by wall-clock changes.
        var started = timeProvider.GetTimestamp();

        while (true)
        {
            var existing = await transactions.FindByIdempotencyKeyAsync(command.IdempotencyKey, ct);
            if (existing is not null && TryGetCompletedResult(existing, command) is { } result)
                return result;

            if (timeProvider.GetElapsedTime(started) >= timeout)
                throw new CheckoutInProgressException(command.IdempotencyKey);

            await Task.Delay(pollInterval, timeProvider, ct);
        }
    }

    /// <returns>The stored result if the row is complete; null if it is still in progress.</returns>
    private CheckoutResult? TryGetCompletedResult(Transaction existing, CheckoutCommand command)
    {
        // Same key, different checkout: a client bug, not a retry.
        if (existing.CustomerId != command.CustomerId || existing.ProductId != command.ProductId)
            throw new IdempotencyKeyReuseException(command.IdempotencyKey);

        var isComplete = existing.Status != TransactionStatus.Pending || existing.RedirectUrl is not null;
        if (!isComplete)
            return null;

        logger.LogInformation("Replayed existing transaction {InvoiceNo} for idempotency key {IdempotencyKey}",
            existing.InvoiceNo, command.IdempotencyKey);
        return ToResult(existing);
    }

    private static string LockKey(string idempotencyKey) => "lock:checkout:" + idempotencyKey;

    private PaymentTokenRequest BuildPaymentTokenRequest(Transaction transaction, Product product)
    {
        var app = appOptions.Value;
        var invoiceNo = transaction.InvoiceNo;

        return new PaymentTokenRequest(
            InvoiceNo: invoiceNo,
            Description: ToDescription(product.Name),
            Amount: transaction.Amount,
            CurrencyCode: transaction.Currency,
            FrontendReturnUrl: $"{app.FrontendBaseUrl.TrimEnd('/')}/result?invoiceNo={Uri.EscapeDataString(invoiceNo)}",
            BackendReturnUrl: $"{app.PublicApiBaseUrl.TrimEnd('/')}/api/payments/2c2p/notification",
            Intent: product.PaymentType switch
            {
                PaymentType.OneTime => PaymentIntent.NewPaymentMethod,
                // Recurring products save the card so later charges can use the token.
                PaymentType.Recurring => PaymentIntent.NewCardAndSave,
                _ => throw new InvalidOperationException($"Unknown payment type {product.PaymentType}."),
            });
    }

    private static CheckoutResult ToResult(Transaction transaction) =>
        new(transaction.InvoiceNo,
            transaction.Status,
            transaction.Status == TransactionStatus.Pending ? transaction.RedirectUrl : null);

    // INV + UTC date + 8 random hex characters, e.g. INV20261015A1B2C3D4 (19 chars).
    // The unique index on invoice_no is the final guard against a (very unlikely) collision.
    private string NewInvoiceNo() =>
        $"INV{timeProvider.GetUtcNow():yyyyMMdd}{RandomNumberGenerator.GetHexString(8)}";

    // HTML-encoded, then cut to the limit without splitting an entity such as "&amp;".
    private static string ToDescription(string productName)
    {
        var encoded = WebUtility.HtmlEncode(productName);
        if (encoded.Length <= MaxDescriptionLength)
            return encoded;

        var cut = encoded[..MaxDescriptionLength];
        var lastAmp = cut.LastIndexOf('&');
        return lastAmp >= 0 && cut.IndexOf(';', lastAmp) < 0 ? cut[..lastAmp] : cut;
    }
}
