using Checkout.Domain.Entities;
using Checkout.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using static Checkout.Tests.Integration.IntegrationHelpers;

namespace Checkout.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class DataModelTests(IntegrationFixture fixture)
{
    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private static Transaction NewTransaction(string invoiceNo, string key, decimal amount = 10.00m) => new()
    {
        CustomerId = Alice,
        ProductId = Earbuds,
        InvoiceNo = invoiceNo,
        IdempotencyKey = key,
        Amount = amount,
        Currency = "SGD",
        Status = TransactionStatus.Pending,
        CreatedAt = DateTime.UtcNow,
    };

    // Each save on its own DbContext, as two separate requests would.
    private static Task SaveAsync(CheckoutApiFactory app, object entity) =>
        app.QueryDbAsync(async db =>
        {
            db.Add(entity);
            return await db.SaveChangesAsync();
        });

    private static async Task AssertDuplicateKey(Func<Task> save)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(save);
        var mysql = Assert.IsType<MySqlException>(ex.InnerException);
        Assert.Equal(1062, mysql.Number);
    }

    // ---- unique indexes ----

    [Fact]
    public async Task Duplicate_invoice_no_is_rejected_by_mysql()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var invoiceNo = Unique("INV");

        await SaveAsync(app, NewTransaction(invoiceNo, NewKey()));

        await AssertDuplicateKey(() => SaveAsync(app, NewTransaction(invoiceNo, NewKey())));
    }

    [Fact]
    public async Task Duplicate_idempotency_key_is_rejected_by_mysql()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var key = NewKey();

        await SaveAsync(app, NewTransaction(Unique("INV"), key));

        await AssertDuplicateKey(() => SaveAsync(app, NewTransaction(Unique("INV"), key)));
    }

    [Fact]
    public async Task Duplicate_customer_token_for_a_customer_is_rejected_by_mysql()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var token = Unique("CT_");
        CustomerCard Card() => new()
            { CustomerId = Alice, CustomerToken = token, MaskedCardNo = "411111XXXXXX1111", CreatedAt = DateTime.UtcNow };

        await SaveAsync(app, Card());

        await AssertDuplicateKey(() => SaveAsync(app, Card()));
    }

    // ---- money ----

    [Fact]
    public async Task Decimal_amount_round_trips_exactly()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var invoiceNo = Unique("INV");

        await SaveAsync(app, NewTransaction(invoiceNo, NewKey(), amount: 19.99m));

        Assert.Equal(19.99m, (await TransactionByInvoice(app, invoiceNo)).Amount);
    }

    [Theory]
    [InlineData("transaction", "amount")]
    [InlineData("product", "price")]
    public async Task Money_columns_are_decimal_12_2(string table, string column)
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());

        var type = await app.QueryDbAsync(db => db.Database.SqlQuery<string>($"""
            SELECT COLUMN_TYPE AS Value FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = {table} AND COLUMN_NAME = {column}
            """).SingleAsync());

        Assert.Equal("decimal(12,2)", type);
    }

    // ---- no card data we must not hold ----

    [Fact]
    public async Task No_column_can_hold_a_full_card_number_or_cvv()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());

        var columns = await app.QueryDbAsync(db => db.Database.SqlQuery<string>($"""
            SELECT COLUMN_NAME AS Value FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME IN ('customer_card', 'transaction')
            """).ToListAsync());

        Assert.NotEmpty(columns);
        Assert.DoesNotContain(columns, c => c.Contains("cvv", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, c => c is "card_no" or "card_number" or "pan");
        // The only column holding card digits is the masked one (customer_card_id is a foreign key).
        Assert.All(columns.Where(c => c.Contains("card", StringComparison.OrdinalIgnoreCase) && c != "customer_card_id"),
            c => Assert.Equal("masked_card_no", c));
    }

    // ---- caching ----

    [Fact]
    public async Task Customers_are_cached_with_a_ttl_but_transactions_never_are()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var client = app.CreateClient();
        var redis = fixture.Redis.GetDatabase();
        await redis.KeyDeleteAsync("customers:all");

        (await client.GetAsync("/api/customers")).EnsureSuccessStatusCode();

        Assert.True(await redis.KeyExistsAsync("customers:all"));
        var ttl = await redis.KeyTimeToLiveAsync("customers:all");
        Assert.NotNull(ttl);
        Assert.True(ttl > TimeSpan.Zero);

        var checkout = await ReadCheckout(await PostCheckout(client, NewKey(), Alice, Earbuds));
        (await client.GetAsync($"/api/transactions/{checkout.InvoiceNo}")).EnsureSuccessStatusCode();

        var keys = await fixture.RedisKeysAsync("*");
        Assert.DoesNotContain(keys, k => k.Contains(checkout.InvoiceNo, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(keys, k => k.Contains("transaction", StringComparison.OrdinalIgnoreCase));
    }
}
