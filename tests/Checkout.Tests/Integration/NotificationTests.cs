using System.Net;
using System.Net.Http.Json;
using Checkout.Domain.Enums;
using static Checkout.Tests.Integration.IntegrationHelpers;

namespace Checkout.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class NotificationTests(IntegrationFixture fixture)
{
    private static string NewCustomerToken() => "CT_" + Guid.NewGuid().ToString("N");

    // ---- a. duplicates at the same moment ----

    [Fact]
    public async Task Parallel_identical_success_notifications_apply_once_and_save_one_card()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var client = app.CreateClient();
        var tx = await CreatePendingTransaction(app, client);
        var customerToken = NewCustomerToken();
        var body = SignedNotification(ResultFields(tx.InvoiceNo, tx.Amount, customerToken: customerToken));

        var responses = await RunTogether(10, _ => PostNotification(client, body));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var stored = await TransactionByInvoice(app, tx.InvoiceNo);
        Assert.Equal(TransactionStatus.Succeeded, stored.Status);
        Assert.Equal("CC", stored.PaymentChannel);
        Assert.Equal("411111XXXXXX1111", stored.MaskedCardNo);
        Assert.NotNull(stored.ProviderRef);
        Assert.NotNull(stored.PaidAt);
        Assert.Equal(1, await CardCount(app, customerToken));
    }

    // ---- b. a final status is never overwritten ----

    [Fact]
    public async Task Succeeded_then_failure_notification_stays_Succeeded()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var client = app.CreateClient();
        var tx = await CreatePendingTransaction(app, client);

        (await PostNotification(client, SignedNotification(ResultFields(tx.InvoiceNo, tx.Amount)))).EnsureSuccessStatusCode();
        var late = await PostNotification(client, SignedNotification(ResultFields(tx.InvoiceNo, tx.Amount, respCode: "4012")));

        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
        var stored = await TransactionByInvoice(app, tx.InvoiceNo);
        Assert.Equal(TransactionStatus.Succeeded, stored.Status);
        Assert.Equal("0000", stored.RespCode);
    }

    [Fact]
    public async Task Failed_then_success_notification_stays_Failed_and_saves_no_card()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var client = app.CreateClient();
        var tx = await CreatePendingTransaction(app, client);
        var customerToken = NewCustomerToken();

        (await PostNotification(client, SignedNotification(ResultFields(tx.InvoiceNo, tx.Amount, respCode: "4012")))).EnsureSuccessStatusCode();
        var late = await PostNotification(client,
            SignedNotification(ResultFields(tx.InvoiceNo, tx.Amount, customerToken: customerToken)));

        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
        var stored = await TransactionByInvoice(app, tx.InvoiceNo);
        Assert.Equal(TransactionStatus.Failed, stored.Status);
        Assert.Null(stored.PaidAt);
        Assert.Equal(0, await CardCount(app, customerToken));
    }

    // ---- c. what is rejected or ignored ----

    [Fact]
    public async Task Tampered_signature_is_400_and_the_row_is_unchanged()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var client = app.CreateClient();
        var tx = await CreatePendingTransaction(app, client);
        var jwt = TestJwt.Sign(new Dictionary<string, object?>(ResultFields(tx.InvoiceNo, tx.Amount))
            { ["merchantID"] = IntegrationFixture.MerchantId }, IntegrationFixture.SecretKey);
        // Flip the first signature character to a different valid base64url character.
        var dot = jwt.LastIndexOf('.');
        var tampered = jwt[..(dot + 1)] + (jwt[dot + 1] == 'A' ? 'B' : 'A') + jwt[(dot + 2)..];

        var response = await PostNotification(client, new { payload = tampered });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var stored = await TransactionByInvoice(app, tx.InvoiceNo);
        Assert.Equal(TransactionStatus.Pending, stored.Status);
        Assert.Null(stored.RespCode);
        Assert.Null(stored.PaidAt);
    }

    [Fact]
    public async Task Wrong_amount_is_acknowledged_but_not_applied()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var client = app.CreateClient();
        var tx = await CreatePendingTransaction(app, client);

        var response = await PostNotification(client, SignedNotification(ResultFields(tx.InvoiceNo, 0.01m)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TransactionStatus.Pending, (await TransactionByInvoice(app, tx.InvoiceNo)).Status);
    }

    [Fact]
    public async Task Unknown_invoice_is_acknowledged()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());

        var response = await PostNotification(app.CreateClient(),
            SignedNotification(ResultFields("INV_UNKNOWN_" + Guid.NewGuid().ToString("N")[..8], 10.00m)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Success_without_customer_token_saves_no_card()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var client = app.CreateClient();
        var tx = await CreatePendingTransaction(app, client);
        var cardsBefore = await app.QueryDbAsync(db => Task.FromResult(db.CustomerCards.Count()));

        (await PostNotification(client, SignedNotification(ResultFields(tx.InvoiceNo, tx.Amount)))).EnsureSuccessStatusCode();

        Assert.Equal(TransactionStatus.Succeeded, (await TransactionByInvoice(app, tx.InvoiceNo)).Status);
        Assert.Equal(cardsBefore, await app.QueryDbAsync(db => Task.FromResult(db.CustomerCards.Count())));
    }

    // ---- d. the status endpoint shows it at once ----

    [Fact]
    public async Task Status_endpoint_returns_the_fresh_status_with_no_store()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var client = app.CreateClient();
        var tx = await CreatePendingTransaction(app, client);

        var before = await client.GetAsync($"/api/transactions/{tx.InvoiceNo}");
        Assert.Equal("Pending", (await before.Content.ReadFromJsonAsync<StatusResponse>())!.Status);

        (await PostNotification(client, SignedNotification(ResultFields(tx.InvoiceNo, tx.Amount)))).EnsureSuccessStatusCode();
        var after = await client.GetAsync($"/api/transactions/{tx.InvoiceNo}");

        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.True(after.Headers.CacheControl?.NoStore, "Cache-Control: no-store expected");
        var status = (await after.Content.ReadFromJsonAsync<StatusResponse>())!;
        Assert.Equal("Succeeded", status.Status);
        Assert.Equal("411111XXXXXX1111", status.MaskedCardNo);
        Assert.NotNull(status.PaidAt);
    }

    // ---- converted currency, end to end (what the 2C2P demo merchant actually sends) ----

    [Fact]
    public async Task Success_settled_in_another_currency_is_applied_when_the_fx_amount_matches()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var client = app.CreateClient();
        var tx = await CreatePendingTransaction(app, client);
        var customerToken = NewCustomerToken();
        var fields = ResultFields(tx.InvoiceNo, tx.Amount * 24.2354868m, customerToken: customerToken);
        fields["currencyCode"] = "THB";
        fields["fxAmount"] = tx.Amount;
        fields["fxCurrencyCode"] = "SGD";

        var response = await PostNotification(client, SignedNotification(fields));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TransactionStatus.Succeeded, (await TransactionByInvoice(app, tx.InvoiceNo)).Status);
        Assert.Equal(1, await CardCount(app, customerToken));
    }
}
