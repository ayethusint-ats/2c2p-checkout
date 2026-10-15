using Checkout.Application.Checkout;
using Checkout.Application.Common;
using Checkout.Application.Payments;
using Checkout.Domain.Entities;
using Checkout.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Checkout.Tests.Checkout;

public class CheckoutServiceTests
{
    private const int CustomerId = 1;
    private const int OneTimeProductId = 10;
    private const int RecurringProductId = 20;
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 7, 30, 0, TimeSpan.Zero);

    private readonly FakeCustomerRepository _customers = new();
    private readonly FakeProductRepository _products = new();
    private readonly FakeTransactionRepository _transactions = new();
    private readonly FakePaymentProvider _provider = new();
    private readonly FakeDistributedLock _lock = new();
    private readonly CheckoutService _service;

    public CheckoutServiceTests()
    {
        _customers.Customers[CustomerId] = new Customer
            { Id = CustomerId, FirstName = "Alice", LastName = "Tan", Email = "alice.tan@example.com" };
        _products.Products[OneTimeProductId] = new Product
        {
            Id = OneTimeProductId, Name = "Wireless Earbuds", Description = "d",
            Price = 89.90m, Currency = "SGD", PaymentType = PaymentType.OneTime,
        };
        _products.Products[RecurringProductId] = new Product
        {
            Id = RecurringProductId, Name = "Cloud Storage Monthly", Description = "d",
            Price = 4.99m, Currency = "SGD", PaymentType = PaymentType.Recurring,
        };

        _service = new CheckoutService(
            _customers, _products, _transactions, _provider, _lock,
            new FixedTimeProvider(Now),
            Options.Create(new AppOptions
            {
                FrontendBaseUrl = "http://localhost:3000/",
                PublicApiBaseUrl = "https://abc.tunnel.example",
            }),
            // Tiny wait/poll values so timeout tests finish quickly.
            Options.Create(new CheckoutOptions
            {
                LockTtlSeconds = 45,
                WaitTimeoutSeconds = 0.2,
                PollIntervalMilliseconds = 10,
            }),
            NullLogger<CheckoutService>.Instance);
    }

    private static CheckoutCommand Command(int productId = OneTimeProductId, int customerId = CustomerId,
        int? cardId = null, string key = "6f1c2c1e-7d1b-4a37-9b1a-0e5d1b9e2f11") =>
        new(customerId, productId, cardId, key);

    // ---- intent mapping ----

    [Fact]
    public async Task OneTime_product_uses_NewPaymentMethod()
    {
        await _service.CheckoutAsync(Command(OneTimeProductId), CancellationToken.None);

        Assert.Equal(PaymentIntent.NewPaymentMethod, Assert.Single(_provider.Requests).Intent);
    }

    [Fact]
    public async Task Recurring_product_uses_NewCardAndSave()
    {
        await _service.CheckoutAsync(Command(RecurringProductId), CancellationToken.None);

        Assert.Equal(PaymentIntent.NewCardAndSave, Assert.Single(_provider.Requests).Intent);
    }

    // ---- outcomes ----

    [Fact]
    public async Task Success_stores_token_and_redirect_url_and_stays_Pending()
    {
        var result = await _service.CheckoutAsync(Command(), CancellationToken.None);

        var tx = Assert.Single(_transactions.Saved);
        Assert.Equal(TransactionStatus.Pending, tx.Status);
        Assert.Equal("PT_123", tx.PaymentToken);
        Assert.Equal("https://sandbox-pgw-ui.2c2p.com/payment/4.3/#/token/PT_123", tx.RedirectUrl);

        Assert.Equal(tx.InvoiceNo, result.InvoiceNo);
        Assert.Equal(TransactionStatus.Pending, result.Status);
        Assert.Equal(tx.RedirectUrl, result.RedirectUrl);
    }

    [Fact]
    public async Task Non_0000_result_stores_Failed_with_provider_codes()
    {
        _provider.Respond = _ => new PaymentTokenResult(false, null, null, "9042", "Invalid currency code.");

        var result = await _service.CheckoutAsync(Command(), CancellationToken.None);

        var tx = Assert.Single(_transactions.Saved);
        Assert.Equal(TransactionStatus.Failed, tx.Status);
        Assert.Equal("9042", tx.RespCode);
        Assert.Equal("Invalid currency code.", tx.RespDesc);
        Assert.Null(tx.PaymentToken);
        Assert.Equal(TransactionStatus.Failed, result.Status);
        Assert.Null(result.RedirectUrl);
    }

    [Fact]
    public async Task Provider_exception_stores_Failed_ERROR()
    {
        _provider.Respond = _ => throw new PaymentProviderException("timed out");

        var result = await _service.CheckoutAsync(Command(), CancellationToken.None);

        var tx = Assert.Single(_transactions.Saved);
        Assert.Equal(TransactionStatus.Failed, tx.Status);
        Assert.Equal("ERROR", tx.RespCode);
        Assert.Equal("Payment provider unavailable", tx.RespDesc);
        Assert.Equal(TransactionStatus.Failed, result.Status);
        Assert.Null(result.RedirectUrl);
    }

    [Fact]
    public async Task Pending_transaction_is_saved_before_the_provider_is_called()
    {
        Transaction? savedAtCallTime = null;
        _provider.OnCall = () => savedAtCallTime = _transactions.Saved.SingleOrDefault();

        await _service.CheckoutAsync(Command(), CancellationToken.None);

        Assert.NotNull(savedAtCallTime);
        Assert.Equal(_provider.Requests[0].InvoiceNo, savedAtCallTime.InvoiceNo);
    }

    // ---- idempotency ----

    [Fact]
    public async Task Same_idempotency_key_calls_provider_once_and_returns_same_result()
    {
        var first = await _service.CheckoutAsync(Command(), CancellationToken.None);
        var second = await _service.CheckoutAsync(Command(), CancellationToken.None);

        Assert.Single(_provider.Requests);
        Assert.Single(_transactions.Saved);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Replay_of_a_failed_checkout_returns_Failed_without_calling_provider_again()
    {
        _provider.Respond = _ => new PaymentTokenResult(false, null, null, "9042", "Invalid currency code.");
        var first = await _service.CheckoutAsync(Command(), CancellationToken.None);

        _provider.Respond = _ => throw new Xunit.Sdk.XunitException("provider must not be called on replay");
        var second = await _service.CheckoutAsync(Command(), CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Single(_provider.Requests);
    }

    [Fact]
    public async Task Different_idempotency_keys_create_separate_transactions()
    {
        await _service.CheckoutAsync(Command(key: "11111111-1111-1111-1111-111111111111"), CancellationToken.None);
        await _service.CheckoutAsync(Command(key: "22222222-2222-2222-2222-222222222222"), CancellationToken.None);

        Assert.Equal(2, _provider.Requests.Count);
        Assert.NotEqual(_transactions.Saved[0].InvoiceNo, _transactions.Saved[1].InvoiceNo);
    }

    // ---- validation ----

    [Fact]
    public async Task Unknown_customer_throws_NotFound_and_saves_nothing()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.CheckoutAsync(Command(customerId: 999), CancellationToken.None));

        Assert.Empty(_transactions.Saved);
        Assert.Empty(_provider.Requests);
    }

    [Fact]
    public async Task Unknown_product_throws_NotFound_and_saves_nothing()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.CheckoutAsync(Command(productId: 999), CancellationToken.None));

        Assert.Empty(_transactions.Saved);
        Assert.Empty(_provider.Requests);
    }

    [Fact]
    public async Task Customer_card_id_is_rejected_as_not_implemented()
    {
        await Assert.ThrowsAsync<FeatureNotImplementedException>(() =>
            _service.CheckoutAsync(Command(cardId: 5), CancellationToken.None));

        Assert.Empty(_transactions.Saved);
        Assert.Empty(_provider.Requests);
    }

    // ---- what is sent and stored ----

    [Fact]
    public async Task Amount_and_currency_come_from_the_product()
    {
        await _service.CheckoutAsync(Command(OneTimeProductId), CancellationToken.None);

        var request = Assert.Single(_provider.Requests);
        Assert.Equal(89.90m, request.Amount);
        Assert.Equal("SGD", request.CurrencyCode);

        var tx = Assert.Single(_transactions.Saved);
        Assert.Equal(89.90m, tx.Amount);
        Assert.Equal("SGD", tx.Currency);
    }

    [Fact]
    public async Task Invoice_number_return_urls_and_created_at_are_built_from_config_and_clock()
    {
        await _service.CheckoutAsync(Command(), CancellationToken.None);

        var tx = Assert.Single(_transactions.Saved);
        var request = Assert.Single(_provider.Requests);

        Assert.Matches("^INV20261015[0-9A-F]{8}$", tx.InvoiceNo);
        Assert.Equal(Now.UtcDateTime, tx.CreatedAt);
        Assert.Equal($"http://localhost:3000/result?invoiceNo={tx.InvoiceNo}", request.FrontendReturnUrl);
        Assert.Equal("https://abc.tunnel.example/api/payments/2c2p/notification", request.BackendReturnUrl);
    }

    [Fact]
    public async Task Description_is_the_html_encoded_product_name_capped_at_250_chars()
    {
        _products.Products[OneTimeProductId].Name = "Tom & Jerry <Deluxe>";
        await _service.CheckoutAsync(Command(key: "11111111-1111-1111-1111-111111111111"), CancellationToken.None);
        Assert.Equal("Tom &amp; Jerry &lt;Deluxe&gt;", _provider.Requests[^1].Description);

        // 246 chars + "&amp;" would cross 250: the entity is dropped, not cut in half.
        _products.Products[OneTimeProductId].Name = new string('a', 246) + "&b";
        await _service.CheckoutAsync(Command(key: "22222222-2222-2222-2222-222222222222"), CancellationToken.None);
        Assert.Equal(new string('a', 246), _provider.Requests[^1].Description);
    }

    // ---- Step 6: lock, wait, and the database as the final guard ----

    private const string Key = "6f1c2c1e-7d1b-4a37-9b1a-0e5d1b9e2f11";

    private static Transaction StoredTransaction(
        TransactionStatus status = TransactionStatus.Pending, string? redirectUrl = "https://pay/winner",
        int productId = OneTimeProductId) => new()
    {
        CustomerId = CustomerId,
        ProductId = productId,
        InvoiceNo = "INV20261015WINNER01",
        IdempotencyKey = Key,
        Amount = 89.90m,
        Currency = "SGD",
        Status = status,
        RedirectUrl = redirectUrl,
        PaymentToken = redirectUrl is null ? null : "PT_WINNER",
    };

    [Fact]
    public async Task Lock_key_is_derived_from_the_idempotency_key()
    {
        await _service.CheckoutAsync(Command(), CancellationToken.None);

        Assert.Equal($"lock:checkout:{Key}", Assert.Single(_lock.Keys));
    }

    [Fact]
    public async Task Same_key_with_a_different_product_throws_reuse()
    {
        await _service.CheckoutAsync(Command(OneTimeProductId), CancellationToken.None);

        await Assert.ThrowsAsync<IdempotencyKeyReuseException>(() =>
            _service.CheckoutAsync(Command(RecurringProductId), CancellationToken.None));
        Assert.Single(_provider.Requests);
    }

    [Fact]
    public async Task Lock_busy_returns_the_result_once_the_other_request_completes()
    {
        _lock.Behaviour = LockBehaviour.Busy;
        // The other request's row appears (complete) on the 3rd lookup, i.e. while we are waiting.
        _transactions.BeforeFind = call =>
        {
            if (call == 3)
                _transactions.Saved.Add(StoredTransaction());
        };

        var result = await _service.CheckoutAsync(Command(), CancellationToken.None);

        Assert.Equal(new CheckoutResult("INV20261015WINNER01", TransactionStatus.Pending, "https://pay/winner"), result);
        Assert.Empty(_provider.Requests);
    }

    [Fact]
    public async Task Lock_busy_and_no_result_in_time_throws_in_progress()
    {
        _lock.Behaviour = LockBehaviour.Busy;

        await Assert.ThrowsAsync<CheckoutInProgressException>(() =>
            _service.CheckoutAsync(Command(), CancellationToken.None));

        Assert.Empty(_provider.Requests);
        Assert.True(_transactions.FindCount > 2, "should have polled more than once");
    }

    [Fact]
    public async Task Stuck_pending_row_without_redirect_is_never_retried()
    {
        // Pending, no payment page, and nobody holds the lock: the original request died.
        _transactions.Saved.Add(StoredTransaction(redirectUrl: null));

        await Assert.ThrowsAsync<CheckoutInProgressException>(() =>
            _service.CheckoutAsync(Command(), CancellationToken.None));

        Assert.Empty(_provider.Requests);
        Assert.Empty(_lock.Keys);
    }

    [Fact]
    public async Task Redis_down_still_checks_out_once_without_a_lock()
    {
        _lock.Behaviour = LockBehaviour.Throw;

        var result = await _service.CheckoutAsync(Command(), CancellationToken.None);

        Assert.Single(_provider.Requests);
        var tx = Assert.Single(_transactions.Saved);
        Assert.Equal(TransactionStatus.Pending, tx.Status);
        Assert.Equal(tx.RedirectUrl, result.RedirectUrl);
    }

    [Fact]
    public async Task Redis_down_and_database_rejects_duplicate_returns_the_winners_result()
    {
        _lock.Behaviour = LockBehaviour.Throw;
        _transactions.DuplicateOnSaveWinner = StoredTransaction();

        var result = await _service.CheckoutAsync(Command(), CancellationToken.None);

        Assert.Equal(new CheckoutResult("INV20261015WINNER01", TransactionStatus.Pending, "https://pay/winner"), result);
        Assert.Empty(_provider.Requests);
        Assert.Single(_transactions.Saved);
    }

    [Fact]
    public async Task Lock_is_released_after_success()
    {
        await _service.CheckoutAsync(Command(), CancellationToken.None);

        Assert.Equal(1, Assert.Single(_lock.Handles).DisposeCount);
    }

    [Fact]
    public async Task Lock_is_released_after_a_failed_payment_token_result()
    {
        _provider.Respond = _ => new PaymentTokenResult(false, null, null, "9042", "Invalid currency code.");

        await _service.CheckoutAsync(Command(), CancellationToken.None);

        Assert.Equal(1, Assert.Single(_lock.Handles).DisposeCount);
    }

    [Fact]
    public async Task Lock_is_released_after_a_provider_exception()
    {
        _provider.Respond = _ => throw new PaymentProviderException("timed out");

        await _service.CheckoutAsync(Command(), CancellationToken.None);

        Assert.Equal(1, Assert.Single(_lock.Handles).DisposeCount);
    }

    [Fact]
    public async Task Lock_is_released_when_validation_throws()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.CheckoutAsync(Command(productId: 999), CancellationToken.None));

        Assert.Equal(1, Assert.Single(_lock.Handles).DisposeCount);
    }

    [Fact]
    public async Task Provider_and_outcome_save_ignore_client_cancellation_once_the_row_exists()
    {
        using var cts = new CancellationTokenSource();
        // The client disconnects while 2C2P is being called.
        _provider.OnCall = cts.Cancel;

        var result = await _service.CheckoutAsync(Command(), cts.Token);

        Assert.Equal(TransactionStatus.Pending, result.Status);
        Assert.NotNull(Assert.Single(_transactions.Saved).RedirectUrl);
    }
}

