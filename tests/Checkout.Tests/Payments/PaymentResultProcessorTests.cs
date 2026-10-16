using Checkout.Application.Payments;
using Checkout.Domain.Entities;
using Checkout.Domain.Enums;
using Checkout.Tests.Checkout;
using Microsoft.Extensions.Logging.Abstractions;

namespace Checkout.Tests.Payments;

public class PaymentResultProcessorTests
{
    private const string InvoiceNo = "INV20261015A1B2C3D4";
    private const int OwnerCustomerId = 7;
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTransactionRepository _transactions = new();
    private readonly FakeCustomerCardRepository _cards = new();
    private readonly PaymentResultProcessor _processor;
    private readonly Transaction _tx;

    public PaymentResultProcessorTests()
    {
        _tx = new Transaction
        {
            Id = 42,
            CustomerId = OwnerCustomerId,
            ProductId = 3,
            InvoiceNo = InvoiceNo,
            IdempotencyKey = "k",
            Amount = 89.90m,
            Currency = "SGD",
            Status = TransactionStatus.Pending,
            PaymentToken = "PT",
            RedirectUrl = "https://pay",
        };
        _transactions.Saved.Add(_tx);

        _processor = new PaymentResultProcessor(
            _transactions, _cards, new FixedTimeProvider(Now), NullLogger<PaymentResultProcessor>.Instance);
    }

    private static PaymentResultInfo Result(
        string respCode = "0000", string respDesc = "Success", decimal amount = 89.90m, string currency = "SGD",
        string? customerToken = "CUST_TOKEN_ABC", string invoiceNo = InvoiceNo) =>
        new("JT01", invoiceNo, amount, currency, respCode, respDesc,
            ChannelCode: "VI", AccountNo: "411111XXXXXX1111", TranRef: "4589123", CustomerToken: customerToken);

    // ---- applying a result ----

    [Fact]
    public async Task Success_marks_Succeeded_with_channel_card_ref_and_paid_at()
    {
        var outcome = await _processor.ApplyAsync(Result(), CancellationToken.None);

        Assert.Equal(PaymentResultOutcome.Applied, outcome);
        Assert.Equal(TransactionStatus.Succeeded, _tx.Status);
        Assert.Equal("VI", _tx.PaymentChannel);
        Assert.Equal("411111XXXXXX1111", _tx.MaskedCardNo);
        Assert.Equal("4589123", _tx.ProviderRef);
        Assert.Equal("0000", _tx.RespCode);
        Assert.Equal("Success", _tx.RespDesc);
        Assert.Equal(Now.UtcDateTime, _tx.PaidAt);
    }

    [Fact]
    public async Task Non_0000_marks_Failed_with_code_and_description()
    {
        var outcome = await _processor.ApplyAsync(Result("4012", "Transaction declined"), CancellationToken.None);

        Assert.Equal(PaymentResultOutcome.Applied, outcome);
        Assert.Equal(TransactionStatus.Failed, _tx.Status);
        Assert.Equal("4012", _tx.RespCode);
        Assert.Equal("Transaction declined", _tx.RespDesc);
        Assert.Null(_tx.PaidAt);
        Assert.Empty(_cards.Cards);
    }

    [Fact]
    public async Task Same_notification_twice_is_AlreadyFinal_and_saves_one_card()
    {
        await _processor.ApplyAsync(Result(), CancellationToken.None);
        var paidAt = _tx.PaidAt;

        var second = await _processor.ApplyAsync(Result(), CancellationToken.None);

        Assert.Equal(PaymentResultOutcome.AlreadyFinal, second);
        Assert.Equal(TransactionStatus.Succeeded, _tx.Status);
        Assert.Equal(paidAt, _tx.PaidAt);
        Assert.Single(_cards.Cards);
    }

    [Fact]
    public async Task Failed_then_a_late_0000_stays_Failed_and_saves_no_card()
    {
        await _processor.ApplyAsync(Result("4012", "Transaction declined"), CancellationToken.None);

        var outcome = await _processor.ApplyAsync(Result(), CancellationToken.None);

        Assert.Equal(PaymentResultOutcome.AlreadyFinal, outcome);
        Assert.Equal(TransactionStatus.Failed, _tx.Status);
        Assert.Equal("4012", _tx.RespCode);
        Assert.Empty(_cards.Cards);
    }

    [Fact]
    public async Task Succeeded_then_a_Failed_notification_stays_Succeeded()
    {
        await _processor.ApplyAsync(Result(), CancellationToken.None);

        var outcome = await _processor.ApplyAsync(Result("4012", "Transaction declined"), CancellationToken.None);

        Assert.Equal(PaymentResultOutcome.AlreadyFinal, outcome);
        Assert.Equal(TransactionStatus.Succeeded, _tx.Status);
        Assert.Equal("0000", _tx.RespCode);
    }

    // ---- rejected results ----

    [Theory]
    [InlineData(89.91, "SGD")]
    [InlineData(0.01, "SGD")]
    [InlineData(89.90, "USD")]
    public async Task Amount_or_currency_mismatch_changes_nothing(decimal amount, string currency)
    {
        var outcome = await _processor.ApplyAsync(Result(amount: amount, currency: currency), CancellationToken.None);

        Assert.Equal(PaymentResultOutcome.AmountOrCurrencyMismatch, outcome);
        Assert.Equal(TransactionStatus.Pending, _tx.Status);
        Assert.Equal(0, _transactions.ConditionalUpdateCount);
        Assert.Empty(_cards.Cards);
    }

    [Fact]
    public async Task Currency_comparison_ignores_case()
    {
        var outcome = await _processor.ApplyAsync(Result(currency: "sgd"), CancellationToken.None);

        Assert.Equal(PaymentResultOutcome.Applied, outcome);
    }

    [Fact]
    public async Task Unknown_invoice_changes_nothing()
    {
        var outcome = await _processor.ApplyAsync(Result(invoiceNo: "INV-NOT-OURS"), CancellationToken.None);

        Assert.Equal(PaymentResultOutcome.UnknownInvoice, outcome);
        Assert.Equal(TransactionStatus.Pending, _tx.Status);
        Assert.Equal(0, _transactions.ConditionalUpdateCount);
    }

    // ---- saved cards ----

    [Fact]
    public async Task Success_with_customer_token_saves_card_for_the_transactions_customer()
    {
        await _processor.ApplyAsync(Result(), CancellationToken.None);

        var card = Assert.Single(_cards.Cards);
        Assert.Equal(OwnerCustomerId, card.CustomerId);
        Assert.Equal("CUST_TOKEN_ABC", card.CustomerToken);
        Assert.Equal("411111XXXXXX1111", card.MaskedCardNo);
        Assert.Equal(Now.UtcDateTime, card.CreatedAt);
    }

    [Fact]
    public async Task Success_without_customer_token_saves_no_card()
    {
        await _processor.ApplyAsync(Result(customerToken: null), CancellationToken.None);

        Assert.Equal(TransactionStatus.Succeeded, _tx.Status);
        Assert.Empty(_cards.Cards);
    }

    [Fact]
    public async Task Retry_after_Succeeded_saves_a_card_that_was_missing()
    {
        // e.g. the first notification updated the status, then the card insert failed (500, 2C2P retries).
        _tx.Status = TransactionStatus.Succeeded;

        var outcome = await _processor.ApplyAsync(Result(), CancellationToken.None);

        Assert.Equal(PaymentResultOutcome.AlreadyFinal, outcome);
        Assert.Equal(OwnerCustomerId, Assert.Single(_cards.Cards).CustomerId);
    }

    [Fact]
    public async Task Over_long_token_saves_no_card()
    {
        await _processor.ApplyAsync(Result(customerToken: new string('T', 101)), CancellationToken.None);

        Assert.Equal(TransactionStatus.Succeeded, _tx.Status);
        Assert.Empty(_cards.Cards);
    }

    // ---- column limits ----

    [Fact]
    public async Task Over_long_text_is_truncated_to_column_sizes()
    {
        var result = Result("4012" + new string('9', 20), new string('d', 150)) with
        {
            ChannelCode = new string('c', 30),
            AccountNo = new string('a', 40),
            TranRef = new string('r', 60),
        };

        await _processor.ApplyAsync(result, CancellationToken.None);

        Assert.Equal(100, _tx.RespDesc!.Length);
        Assert.Equal(10, _tx.RespCode!.Length);

        // Success path for the other three columns.
        var tx2 = new Transaction
        {
            Id = 43, CustomerId = OwnerCustomerId, ProductId = 3, InvoiceNo = "INV2", IdempotencyKey = "k2",
            Amount = 89.90m, Currency = "SGD", Status = TransactionStatus.Pending,
        };
        _transactions.Saved.Add(tx2);
        await _processor.ApplyAsync(result with { InvoiceNo = "INV2", RespCode = "0000" }, CancellationToken.None);

        Assert.Equal(20, tx2.PaymentChannel!.Length);
        Assert.Equal(30, tx2.MaskedCardNo!.Length);
        Assert.Equal(50, tx2.ProviderRef!.Length);
        Assert.Equal(100, tx2.RespDesc!.Length);
    }
}
