using Checkout.Domain.Entities;
using Checkout.Domain.Enums;

namespace Checkout.Tests.Domain;

public class TransactionTests
{
    private static Transaction NewTransaction(TransactionStatus status) => new()
    {
        InvoiceNo = "INV1", IdempotencyKey = "k", Currency = "SGD", Amount = 1m, Status = status,
    };

    [Fact]
    public void AttachPaymentToken_keeps_Pending_and_stores_token_and_url()
    {
        var tx = NewTransaction(TransactionStatus.Pending);

        tx.AttachPaymentToken("PT", "https://pay");

        Assert.Equal(TransactionStatus.Pending, tx.Status);
        Assert.Equal("PT", tx.PaymentToken);
        Assert.Equal("https://pay", tx.RedirectUrl);
    }

    [Fact]
    public void AttachPaymentToken_without_url_then_SetRedirectUrl_keeps_Pending()
    {
        var tx = NewTransaction(TransactionStatus.Pending);

        tx.AttachPaymentToken("PT");
        Assert.Null(tx.RedirectUrl);

        tx.SetRedirectUrl("https://3ds");
        Assert.Equal(TransactionStatus.Pending, tx.Status);
        Assert.Equal("PT", tx.PaymentToken);
        Assert.Equal("https://3ds", tx.RedirectUrl);
    }

    [Fact]
    public void MarkFailed_sets_Failed_with_codes()
    {
        var tx = NewTransaction(TransactionStatus.Pending);

        tx.MarkFailed("9042", "Invalid");

        Assert.Equal(TransactionStatus.Failed, tx.Status);
        Assert.Equal("9042", tx.RespCode);
        Assert.Equal("Invalid", tx.RespDesc);
    }

    [Theory]
    [InlineData(TransactionStatus.Succeeded)]
    [InlineData(TransactionStatus.Failed)]
    public void Status_changes_are_rejected_unless_Pending(TransactionStatus status)
    {
        var tx = NewTransaction(status);

        Assert.Throws<InvalidOperationException>(() => tx.AttachPaymentToken("PT", "https://pay"));
        Assert.Throws<InvalidOperationException>(() => tx.MarkFailed("X", "Y"));
        Assert.Throws<InvalidOperationException>(() => tx.SetRedirectUrl("https://3ds"));
        Assert.Equal(status, tx.Status);
    }
}
