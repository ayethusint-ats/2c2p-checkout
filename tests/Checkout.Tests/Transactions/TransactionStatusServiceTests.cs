using Checkout.Application.Common;
using Checkout.Application.Dtos;
using Checkout.Application.Payments;
using Checkout.Application.Transactions;
using Checkout.Domain.Entities;
using Checkout.Domain.Enums;
using Checkout.Tests.Checkout;
using Microsoft.Extensions.Logging.Abstractions;

namespace Checkout.Tests.Transactions;

public class TransactionStatusServiceTests
{
    private const string InvoiceNo = "INV20261016A1B2C3D4";
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTransactionRepository _transactions = new();
    private readonly FakeCustomerCardRepository _cards = new();
    private readonly FakePaymentProvider _provider = new();
    private readonly TransactionStatusService _service;

    public TransactionStatusServiceTests()
    {
        var processor = new PaymentResultProcessor(
            _transactions, _cards, new FixedTimeProvider(Now), NullLogger<PaymentResultProcessor>.Instance);
        _service = new TransactionStatusService(
            _transactions, _provider, processor, NullLogger<TransactionStatusService>.Instance);
    }

    private Transaction Stored(TransactionStatus status = TransactionStatus.Pending, int? cardId = 5)
    {
        var tx = new Transaction
        {
            Id = 1, CustomerId = 1, ProductId = 10, CustomerCardId = cardId,
            InvoiceNo = InvoiceNo, IdempotencyKey = "k", Amount = 89.90m, Currency = "SGD",
            Status = status, PaymentToken = "PT_SECRET", RedirectUrl = cardId is null ? "https://pay" : null,
            CreatedAt = Now.UtcDateTime,
        };
        _transactions.Saved.Add(tx);
        return tx;
    }

    private static PaymentResultInfo Success(string invoiceNo) =>
        new("JT01", invoiceNo, 89.90m, "SGD", "0000", "Success", "VI", "411111XXXXXX1111", "REF1", null);

    [Fact]
    public async Task Pending_saved_card_payment_is_inquired_once_and_the_fresh_row_returned()
    {
        Stored();
        _provider.InquiryRespond = Success;

        var dto = await _service.GetAsync(InvoiceNo, CancellationToken.None);

        Assert.Equal([InvoiceNo], _provider.Inquiries);
        Assert.Equal("Succeeded", dto.Status);
        Assert.Equal("VI", dto.PaymentChannel);
        Assert.Equal("411111XXXXXX1111", dto.MaskedCardNo);
        Assert.Equal(Now.UtcDateTime, dto.PaidAt);
    }

    [Fact]
    public async Task Pending_hosted_page_payment_is_not_inquired()
    {
        Stored(cardId: null);

        var dto = await _service.GetAsync(InvoiceNo, CancellationToken.None);

        Assert.Empty(_provider.Inquiries);
        Assert.Equal("Pending", dto.Status);
    }

    [Theory]
    [InlineData(TransactionStatus.Succeeded)]
    [InlineData(TransactionStatus.Failed)]
    public async Task Final_payments_are_not_inquired(TransactionStatus status)
    {
        Stored(status);

        var dto = await _service.GetAsync(InvoiceNo, CancellationToken.None);

        Assert.Empty(_provider.Inquiries);
        Assert.Equal(status.ToString(), dto.Status);
    }

    [Fact]
    public async Task Inquiry_exception_is_swallowed_and_the_Pending_row_returned()
    {
        Stored();
        _provider.InquiryRespond = _ => throw new PaymentProviderException("timed out");

        var dto = await _service.GetAsync(InvoiceNo, CancellationToken.None);

        Assert.Single(_provider.Inquiries);
        Assert.Equal("Pending", dto.Status);
    }

    [Fact]
    public async Task Unknown_invoice_is_NotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync("INV_NOPE", CancellationToken.None));
        Assert.Empty(_provider.Inquiries);
    }

    [Fact]
    public void Dto_exposes_no_token_key_or_redirect_url()
    {
        var names = typeof(TransactionStatusDto).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, n => n.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Idempotency", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Redirect", StringComparison.OrdinalIgnoreCase));
    }
}
