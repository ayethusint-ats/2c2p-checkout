using Checkout.Application.Abstractions;
using Checkout.Application.Common;
using Checkout.Application.Dtos;
using Checkout.Application.Payments;
using Checkout.Domain.Entities;
using Checkout.Domain.Enums;

namespace Checkout.Tests.Checkout;

internal sealed class FakeCustomerRepository : ICustomerRepository
{
    public Dictionary<int, Customer> Customers { get; } = new();

    public Task<Customer?> FindAsync(int id, CancellationToken ct) => Task.FromResult(Customers.GetValueOrDefault(id));
    public Task<bool> ExistsAsync(int id, CancellationToken ct) => Task.FromResult(Customers.ContainsKey(id));
    public Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken ct) => throw new NotSupportedException();
}

internal sealed class FakeProductRepository : IProductRepository
{
    public Dictionary<int, Product> Products { get; } = new();

    public Task<Product?> FindAsync(int id, CancellationToken ct) => Task.FromResult(Products.GetValueOrDefault(id));
    public Task<IReadOnlyList<ProductDto>> GetAllAsync(CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>In-memory store: Add makes a row visible only after SaveChanges, like a database.</summary>
internal sealed class FakeTransactionRepository : ITransactionRepository
{
    private readonly List<Transaction> _pending = [];

    public List<Transaction> Saved { get; } = [];
    public int SaveCount { get; private set; }
    public int FindCount { get; private set; }

    /// <summary>Runs before each lookup with the 1-based call number, e.g. to make a row appear mid-wait.</summary>
    public Action<int>? BeforeFind { get; set; }

    /// <summary>
    /// Simulates losing the race at the unique index: the next insert is rejected and this
    /// row (the winner's) becomes visible instead.
    /// </summary>
    public Transaction? DuplicateOnSaveWinner { get; set; }

    public Task<Transaction?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct)
    {
        // Increment separately: in "BeforeFind?.Invoke(++FindCount)" the ++ would be skipped when BeforeFind is null.
        FindCount++;
        BeforeFind?.Invoke(FindCount);
        return Task.FromResult(Saved.SingleOrDefault(t => t.IdempotencyKey == idempotencyKey));
    }

    public Task<Transaction?> FindByInvoiceNoAsync(string invoiceNo, CancellationToken ct) =>
        Task.FromResult(Saved.SingleOrDefault(t => t.InvoiceNo == invoiceNo));

    public Task<TransactionStatusDto?> GetStatusAsync(string invoiceNo, CancellationToken ct) =>
        Task.FromResult(Saved.SingleOrDefault(t => t.InvoiceNo == invoiceNo) is { } t
            ? new TransactionStatusDto(t.InvoiceNo, t.Status.ToString(), t.CustomerId, t.ProductId,
                $"Product {t.ProductId}", t.Amount, t.Currency, t.PaymentChannel, t.MaskedCardNo, t.RespDesc,
                t.PaidAt, t.CreatedAt)
            : null);

    public int ConditionalUpdateCount { get; private set; }

    // Emulates "UPDATE … WHERE id = @id AND status = 'Pending'": changes the row only if Pending.
    public Task<int> MarkSucceededIfPendingAsync(long id, string? paymentChannel, string? maskedCardNo,
        string? providerRef, string respCode, string respDesc, DateTime paidAtUtc, CancellationToken ct)
    {
        ConditionalUpdateCount++;
        var tx = Saved.SingleOrDefault(t => t.Id == id && t.Status == TransactionStatus.Pending);
        if (tx is null)
            return Task.FromResult(0);

        tx.Status = TransactionStatus.Succeeded;
        tx.PaymentChannel = paymentChannel;
        tx.MaskedCardNo = maskedCardNo;
        tx.ProviderRef = providerRef;
        tx.RespCode = respCode;
        tx.RespDesc = respDesc;
        tx.PaidAt = paidAtUtc;
        return Task.FromResult(1);
    }

    public Task<int> MarkFailedIfPendingAsync(long id, string respCode, string respDesc, CancellationToken ct)
    {
        ConditionalUpdateCount++;
        var tx = Saved.SingleOrDefault(t => t.Id == id && t.Status == TransactionStatus.Pending);
        if (tx is null)
            return Task.FromResult(0);

        tx.Status = TransactionStatus.Failed;
        tx.RespCode = respCode;
        tx.RespDesc = respDesc;
        return Task.FromResult(1);
    }

    public Task AddAsync(Transaction transaction, CancellationToken ct)
    {
        _pending.Add(transaction);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveCount++;

        if (DuplicateOnSaveWinner is { } winner && _pending.Count > 0)
        {
            var loser = _pending[0];
            _pending.Clear();
            Saved.Add(winner);
            DuplicateOnSaveWinner = null;
            throw new DuplicateIdempotencyKeyException(loser.IdempotencyKey);
        }

        Saved.AddRange(_pending);
        _pending.Clear();
        return Task.CompletedTask;
    }
}

internal enum LockBehaviour { Acquire, Busy, Throw }

internal sealed class FakeDistributedLock : IDistributedLock
{
    public LockBehaviour Behaviour { get; set; } = LockBehaviour.Acquire;
    public List<FakeLockHandle> Handles { get; } = [];
    public List<string> Keys { get; } = [];

    public Task<ILockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        Keys.Add(key);
        switch (Behaviour)
        {
            case LockBehaviour.Busy:
                return Task.FromResult<ILockHandle?>(null);
            case LockBehaviour.Throw:
                throw new InvalidOperationException("Redis is down (simulated)");
            default:
                var handle = new FakeLockHandle();
                Handles.Add(handle);
                return Task.FromResult<ILockHandle?>(handle);
        }
    }
}

internal sealed class FakeLockHandle : ILockHandle
{
    public int DisposeCount { get; private set; }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakePaymentProvider : IPaymentProvider
{
    public List<PaymentTokenRequest> Requests { get; } = [];

    /// <summary>What to do on each call; defaults to a successful token.</summary>
    public Func<PaymentTokenRequest, PaymentTokenResult> Respond { get; set; } = _ =>
        new PaymentTokenResult(true, "PT_123", "https://sandbox-pgw-ui.2c2p.com/payment/4.3/#/token/PT_123", "0000", "Success");

    /// <summary>Runs at call time, e.g. to inspect what was already saved.</summary>
    public Action? OnCall { get; set; }

    public Task<PaymentTokenResult> CreatePaymentTokenAsync(PaymentTokenRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        OnCall?.Invoke();
        return Task.FromResult(Respond(request));
    }

    public List<(string PaymentToken, string CustomerToken)> DoPaymentCalls { get; } = [];

    /// <summary>Defaults to 2000 ("completed, inquire for the result").</summary>
    public Func<DoPaymentResult> DoPaymentRespond { get; set; } = () =>
        new DoPaymentResult(DoPaymentNextStep.InquireResult, "2000", "Transaction is completed", null);

    public Task<DoPaymentResult> DoPaymentWithSavedCardAsync(string paymentToken, string customerToken, CancellationToken ct)
    {
        DoPaymentCalls.Add((paymentToken, customerToken));
        return Task.FromResult(DoPaymentRespond());
    }

    public List<string> Inquiries { get; } = [];

    /// <summary>Not configured by default: a test that inquires must say what 2C2P answers.</summary>
    public Func<string, PaymentResultInfo> InquiryRespond { get; set; } =
        _ => throw new InvalidOperationException("InquiryRespond is not configured");

    public Task<PaymentResultInfo> InquirePaymentAsync(string invoiceNo, CancellationToken ct)
    {
        Inquiries.Add(invoiceNo);
        return Task.FromResult(InquiryRespond(invoiceNo));
    }

    public PaymentResultInfo? VerifyAndParseNotification(string payload) => throw new NotSupportedException();
}

/// <summary>Emulates the unique index on (customer_id, customer_token).</summary>
internal sealed class FakeCustomerCardRepository : ICustomerCardRepository
{
    public List<CustomerCard> Cards { get; } = [];

    public Task<IReadOnlyList<SavedCardDto>> GetByCustomerIdAsync(int customerId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<CustomerCard?> FindForCustomerAsync(int customerId, int cardId, CancellationToken ct) =>
        Task.FromResult(Cards.SingleOrDefault(c => c.Id == cardId && c.CustomerId == customerId));

    public Task<bool> AddIfNotExistsAsync(int customerId, string customerToken, string maskedCardNo,
        DateTime createdAtUtc, CancellationToken ct)
    {
        if (Cards.Any(c => c.CustomerId == customerId && c.CustomerToken == customerToken))
            return Task.FromResult(false);

        Cards.Add(new CustomerCard
        {
            Id = Cards.Count + 1,
            CustomerId = customerId,
            CustomerToken = customerToken,
            MaskedCardNo = maskedCardNo,
            CreatedAt = createdAtUtc,
        });
        return Task.FromResult(true);
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
