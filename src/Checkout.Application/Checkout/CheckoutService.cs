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
    ICustomerCardRepository cards,
    ITransactionRepository transactions,
    IPaymentProvider paymentProvider,
    IPaymentResultProcessor resultProcessor,
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

            // Ownership is part of the lookup: another customer's card is "not found", with the same
            // message as a missing one, so the response doesn't reveal that the card id exists.
            CustomerCard? card = null;
            if (command.CustomerCardId is { } cardId)
                card = await cards.FindForCustomerAsync(command.CustomerId, cardId, ct)
                    ?? throw new NotFoundException("Saved card", $"{cardId} for customer {command.CustomerId}");

            // Record the attempt before talking to 2C2P. Amount and currency come from the product,
            // never the request. Only FK ids are set: the loaded entities are untracked, and
            // attaching them would make EF try to insert them again.
            var transaction = new Transaction
            {
                CustomerId = command.CustomerId,
                ProductId = product.Id,
                CustomerCardId = card?.Id,
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
            return card is null
                ? await RequestPaymentPageAsync(transaction, product)
                : await PayWithSavedCardAsync(transaction, product, card);
        }
        finally
        {
            if (lockHandle is not null)
                await lockHandle.DisposeAsync();
        }
    }

    private async Task<CheckoutResult> RequestPaymentPageAsync(Transaction transaction, Product product)
    {
        var result = await CreatePaymentTokenOrFailAsync(transaction, BuildPaymentTokenRequest(transaction, product, card: null));
        if (result is null)
            return ToResult(transaction);

        transaction.AttachPaymentToken(result.PaymentToken!, result.RedirectUrl!);
        await transactions.SaveChangesAsync(CancellationToken.None);
        return ToResult(transaction);
    }

    /// <summary>
    /// Payment Token (SavedCard intent), then Do Payment, then act on what 2C2P says to do next.
    /// Once Do Payment has been sent the charge may have happened, so from there on nothing marks the
    /// row Failed: only a verified result (inquiry or notification) can finalise it.
    /// </summary>
    private async Task<CheckoutResult> PayWithSavedCardAsync(Transaction transaction, Product product, CustomerCard card)
    {
        var invoiceNo = transaction.InvoiceNo;

        // c. Nothing has been charged yet, so failures here are final (same handling as the hosted page).
        var tokenResult = await CreatePaymentTokenOrFailAsync(transaction, BuildPaymentTokenRequest(transaction, product, card));
        if (tokenResult is null)
            return ToResult(transaction);

        transaction.AttachPaymentToken(tokenResult.PaymentToken!);
        // f. Saved before anything else touches the row.
        await transactions.SaveChangesAsync(CancellationToken.None);

        // d. The charge itself. An exception here means "outcome unknown", not "failed".
        DoPaymentResult doPayment;
        try
        {
            doPayment = await paymentProvider.DoPaymentWithSavedCardAsync(
                tokenResult.PaymentToken!, card.CustomerToken, CancellationToken.None);
        }
        catch (PaymentProviderException ex)
        {
            logger.LogError(ex, "Do Payment outcome unknown for invoice {InvoiceNo}; leaving it Pending", invoiceNo);
            return new CheckoutResult(invoiceNo, TransactionStatus.Pending, null);
        }

        logger.LogInformation("Do Payment for invoice {InvoiceNo}: respCode {RespCode}, next step {NextStep}",
            invoiceNo, doPayment.RespCode, doPayment.NextStep);

        // e. What 2C2P told us to do next.
        switch (doPayment.NextStep)
        {
            case DoPaymentNextStep.RedirectBrowser:
                transaction.SetRedirectUrl(doPayment.RedirectUrl!);
                await transactions.SaveChangesAsync(CancellationToken.None);
                return ToResult(transaction);

            case DoPaymentNextStep.InquireResult:
                return await InquireAndApplyAsync(invoiceNo);

            default:
                // WaitForResult: the notification (or the status endpoint's inquiry) will finish it.
                return new CheckoutResult(invoiceNo, TransactionStatus.Pending, null);
        }
    }

    private async Task<CheckoutResult> InquireAndApplyAsync(string invoiceNo)
    {
        try
        {
            var result = await paymentProvider.InquirePaymentAsync(invoiceNo, CancellationToken.None);
            // The same processor as the notification: one place decides what a result means.
            await resultProcessor.ApplyAsync(result, CancellationToken.None);
        }
        catch (PaymentProviderException ex)
        {
            logger.LogError(ex, "Payment inquiry failed for invoice {InvoiceNo}; leaving it Pending", invoiceNo);
            return new CheckoutResult(invoiceNo, TransactionStatus.Pending, null);
        }

        // f. The processor updated the database row directly (ExecuteUpdate), so the tracked entity is
        // stale: read the row back, and never save the tracked entity again (it would undo the update).
        var current = await transactions.FindByInvoiceNoAsync(invoiceNo, CancellationToken.None);
        return new CheckoutResult(invoiceNo, current?.Status ?? TransactionStatus.Pending, null);
    }

    /// <summary>
    /// Calls Payment Token. On a rejected request or a provider exception the row is marked Failed and
    /// saved, and null is returned. Nothing can have been charged at this point.
    /// </summary>
    private async Task<PaymentTokenResult?> CreatePaymentTokenOrFailAsync(Transaction transaction, PaymentTokenRequest request)
    {
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
            return null;
        }

        logger.LogInformation("Payment token for invoice {InvoiceNo}: respCode {RespCode}",
            transaction.InvoiceNo, result.RespCode);

        if (result.Succeeded)
            return result;

        transaction.MarkFailed(result.RespCode, result.RespDesc);
        await transactions.SaveChangesAsync(CancellationToken.None);
        return null;
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
            if (existing is not null)
            {
                if (TryGetCompletedResult(existing, command) is { } result)
                    return result;

                // A saved-card charge that has started (token stored) but has no answer yet. While the worker
                // holds the lock its answer is coming, so keep waiting. Once nobody holds it, the worker has
                // finished without a final outcome (e.g. a Do Payment timeout): Pending is then the answer.
                // A 409 here would push the client to a new key, and a new key could charge the card twice.
                if (existing.CustomerCardId is not null && existing.PaymentToken is not null
                    && await TryReadSettledResultAsync(command.IdempotencyKey, ct) is { } settled)
                    return settled;
            }

            if (timeProvider.GetElapsedTime(started) >= timeout)
                throw new CheckoutInProgressException(command.IdempotencyKey);

            await Task.Delay(pollInterval, timeProvider, ct);
        }
    }

    /// <returns>The stored result if the row is complete; null if it is still in progress.</returns>
    private CheckoutResult? TryGetCompletedResult(Transaction existing, CheckoutCommand command)
    {
        // Same key, different checkout: a client bug, not a retry.
        if (existing.CustomerId != command.CustomerId
            || existing.ProductId != command.ProductId
            || existing.CustomerCardId != command.CustomerCardId)
            throw new IdempotencyKeyReuseException(command.IdempotencyKey);

        // Final, or Pending with a page for the customer (hosted page or 3-D Secure). Anything else
        // is still being worked on; WaitForResultAsync decides how long to wait for it.
        var isComplete = existing.Status != TransactionStatus.Pending || existing.RedirectUrl is not null;
        if (!isComplete)
            return null;

        logger.LogInformation("Replayed existing transaction {InvoiceNo} for idempotency key {IdempotencyKey}",
            existing.InvoiceNo, command.IdempotencyKey);
        return ToResult(existing);
    }

    /// <summary>
    /// The row's current state if nobody holds the key's lock (its worker has finished); null if the lock is
    /// held, or if Redis is down so we can't tell (then the caller keeps waiting, as without this check).
    /// </summary>
    private async Task<CheckoutResult?> TryReadSettledResultAsync(string idempotencyKey, CancellationToken ct)
    {
        ILockHandle? lockHandle;
        try
        {
            lockHandle = await distributedLock.TryAcquireAsync(
                LockKey(idempotencyKey), TimeSpan.FromSeconds(checkoutOptions.Value.LockTtlSeconds), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        if (lockHandle is null)
            return null;

        // Re-read while holding the lock: the worker may have finished between our last read and now,
        // and this read is guaranteed to see its final write.
        await using (lockHandle)
        {
            var current = await transactions.FindByIdempotencyKeyAsync(idempotencyKey, ct);
            return current is null ? null : ToResult(current);
        }
    }

    private static string LockKey(string idempotencyKey) => "lock:checkout:" + idempotencyKey;

    private PaymentTokenRequest BuildPaymentTokenRequest(Transaction transaction, Product product, CustomerCard? card)
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
            Intent: card is not null
                // Already saved, so it is charged by token whatever the product type.
                ? PaymentIntent.SavedCard
                : product.PaymentType switch
                {
                    PaymentType.OneTime => PaymentIntent.NewPaymentMethod,
                    // Recurring products save the card so later charges can use the token.
                    PaymentType.Recurring => PaymentIntent.NewCardAndSave,
                    _ => throw new InvalidOperationException($"Unknown payment type {product.PaymentType}."),
                },
            CustomerToken: card?.CustomerToken);
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
