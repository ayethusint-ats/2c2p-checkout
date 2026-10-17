using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Checkout.Domain.Entities;
using Checkout.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Checkout.Tests.Integration;

internal sealed record CheckoutResponse(string InvoiceNo, string Status, string? RedirectUrl);

internal sealed record StatusResponse(string InvoiceNo, string Status, decimal Amount, string CurrencyCode,
    string? PaymentChannel, string? MaskedCardNo, string? RespDesc, DateTime? PaidAt);

internal static class IntegrationHelpers
{
    // Seeded data (CheckoutDbSeeder): customers 1-3; products 1-2 one-time, 3-4 recurring.
    public const int Alice = 1;
    public const int Ben = 2;
    public const int Earbuds = 1;
    public const int ChargingDock = 2;
    public const int CloudStorage = 3;

    public static string NewKey() => Guid.NewGuid().ToString();

    public static Task<HttpResponseMessage> PostCheckout(
        HttpClient client, string? key, int customerId, int productId, int? cardId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { customerId, productId, customerCardId = cardId }),
        };
        if (key is not null)
            request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    public static async Task<CheckoutResponse> ReadCheckout(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<CheckoutResponse>())!;

    /// <summary>A notification body signed like 2C2P does; merchantID defaults to the test merchant.</summary>
    public static object SignedNotification(Dictionary<string, object?> payloadFields)
    {
        var fields = new Dictionary<string, object?> { ["merchantID"] = IntegrationFixture.MerchantId };
        foreach (var (name, value) in payloadFields)
            fields[name] = value;
        return new { payload = TestJwt.Sign(fields, IntegrationFixture.SecretKey) };
    }

    public static Dictionary<string, object?> ResultFields(
        string invoiceNo, decimal amount, string respCode = "0000", string? customerToken = null) => new()
    {
        ["invoiceNo"] = invoiceNo,
        ["amount"] = amount,
        ["currencyCode"] = "SGD",
        ["respCode"] = respCode,
        ["respDesc"] = respCode == "0000" ? "Success" : "Transaction declined",
        ["channelCode"] = "CC",
        ["accountNo"] = "411111XXXXXX1111",
        ["tranRef"] = "TR" + Guid.NewGuid().ToString("N")[..10],
        ["customerToken"] = customerToken,
    };

    public static Task<HttpResponseMessage> PostNotification(HttpClient client, object body) =>
        client.PostAsJsonAsync("/api/payments/2c2p/notification", body);

    /// <summary>
    /// Starts <paramref name="count"/> calls that all wait on one gate, then opens it, so they hit
    /// the API at (as near as possible) the same moment.
    /// </summary>
    public static async Task<T[]> RunTogether<T>(int count, Func<int, Task<T>> call)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, count)
            .Select(async i =>
            {
                await gate.Task;
                return await call(i);
            })
            .ToArray();

        gate.SetResult();
        return await Task.WhenAll(tasks);
    }

    public static Task<List<Transaction>> TransactionsByKey(CheckoutApiFactory app, string key) =>
        app.QueryDbAsync(db => db.Transactions.AsNoTracking().Where(t => t.IdempotencyKey == key).ToListAsync());

    public static Task<Transaction> TransactionByInvoice(CheckoutApiFactory app, string invoiceNo) =>
        app.QueryDbAsync(db => db.Transactions.AsNoTracking().SingleAsync(t => t.InvoiceNo == invoiceNo));

    public static Task<int> CardCount(CheckoutApiFactory app, string customerToken) =>
        app.QueryDbAsync(db => db.CustomerCards.CountAsync(c => c.CustomerToken == customerToken));

    /// <summary>A Pending hosted-page transaction, created through the API.</summary>
    public static async Task<Transaction> CreatePendingTransaction(
        CheckoutApiFactory app, HttpClient client, int customerId = Alice, int productId = CloudStorage)
    {
        var response = await PostCheckout(client, NewKey(), customerId, productId);
        response.EnsureSuccessStatusCode();
        var result = await ReadCheckout(response);
        var tx = await TransactionByInvoice(app, result.InvoiceNo);
        Assert.Equal(TransactionStatus.Pending, tx.Status);
        return tx;
    }

    /// <summary>
    /// Saves a card the way production does: a Recurring checkout, then a signed 0000 notification
    /// carrying a customerToken. Returns the new card's id.
    /// </summary>
    public static async Task<int> CreateSavedCard(CheckoutApiFactory app, HttpClient client, int customerId, string customerToken)
    {
        var tx = await CreatePendingTransaction(app, client, customerId, CloudStorage);
        var notified = await PostNotification(client,
            SignedNotification(ResultFields(tx.InvoiceNo, tx.Amount, customerToken: customerToken)));
        notified.EnsureSuccessStatusCode();

        return await app.QueryDbAsync(db => db.CustomerCards
            .Where(c => c.CustomerId == customerId && c.CustomerToken == customerToken)
            .Select(c => c.Id)
            .SingleAsync());
    }
}
