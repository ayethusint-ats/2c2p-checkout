using Checkout.Application.Abstractions;
using Checkout.Application.Dtos;
using Checkout.Application.Payments;
using Checkout.Domain.Entities;

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

    public Task<Transaction?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct) =>
        Task.FromResult(Saved.SingleOrDefault(t => t.IdempotencyKey == idempotencyKey));

    public Task AddAsync(Transaction transaction, CancellationToken ct)
    {
        _pending.Add(transaction);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        Saved.AddRange(_pending);
        _pending.Clear();
        SaveCount++;
        return Task.CompletedTask;
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
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
