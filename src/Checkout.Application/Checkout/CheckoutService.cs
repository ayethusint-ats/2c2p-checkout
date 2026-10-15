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

public sealed class CheckoutService(
    ICustomerRepository customers,
    IProductRepository products,
    ITransactionRepository transactions,
    IPaymentProvider paymentProvider,
    TimeProvider timeProvider,
    IOptions<AppOptions> appOptions,
    ILogger<CheckoutService> logger) : ICheckoutService
{
    private const int MaxDescriptionLength = 250;

    public async Task<CheckoutResult> CheckoutAsync(CheckoutCommand command, CancellationToken ct)
    {
        // 1. Replay: the same key returns the original outcome and never calls 2C2P again.
        // KNOWN GAP (fixed in Step 6): two concurrent requests with the same key can both miss
        // here. The unique index on idempotency_key then rejects the second insert (HTTP 500),
        // so no duplicate payment is created, but the client gets an error instead of a replay.
        var existing = await transactions.FindByIdempotencyKeyAsync(command.IdempotencyKey, ct);
        if (existing is not null)
            return ToResult(existing);

        // 2. Both must exist. Read uncached, so the price is the current one.
        _ = await customers.FindAsync(command.CustomerId, ct)
            ?? throw new NotFoundException("Customer", command.CustomerId);
        var product = await products.FindAsync(command.ProductId, ct)
            ?? throw new NotFoundException("Product", command.ProductId);

        // 3. Saved-card payments come in Step 8.
        if (command.CustomerCardId is not null)
            throw new FeatureNotImplementedException("Paying with a saved card is not available yet.");

        // 4 + 5. Record the attempt before talking to 2C2P. Amount and currency come from the
        // product, never the request. Only FK ids are set: the loaded entities are untracked,
        // and attaching them would make EF try to insert them again.
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
        await transactions.SaveChangesAsync(ct);

        // 6. Ask 2C2P for a hosted payment page. No DB transaction is open during this call.
        var request = BuildPaymentTokenRequest(transaction, product);
        PaymentTokenResult result;
        try
        {
            result = await paymentProvider.CreatePaymentTokenAsync(request, ct);
        }
        catch (PaymentProviderException ex)
        {
            logger.LogError(ex, "Payment provider unavailable for invoice {InvoiceNo}", transaction.InvoiceNo);
            transaction.MarkFailed("ERROR", "Payment provider unavailable");
            await SaveOutcomeAsync();
            return ToResult(transaction);
        }

        // 7. Record the outcome.
        if (result.Succeeded)
            transaction.AttachPaymentToken(result.PaymentToken!, result.RedirectUrl!);
        else
            transaction.MarkFailed(result.RespCode, result.RespDesc);

        await SaveOutcomeAsync();

        logger.LogInformation("Checkout for invoice {InvoiceNo}: {Status} (respCode {RespCode})",
            transaction.InvoiceNo, transaction.Status, result.RespCode);

        return ToResult(transaction);

        // Once 2C2P has answered, persist that answer even if the client has disconnected meanwhile.
        Task SaveOutcomeAsync() => transactions.SaveChangesAsync(CancellationToken.None);
    }

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
