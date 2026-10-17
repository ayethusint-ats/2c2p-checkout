using System.Net;
using Checkout.Application.Abstractions;
using Checkout.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Checkout.Tests.Integration.IntegrationHelpers;

namespace Checkout.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class CheckoutIdempotencyTests(IntegrationFixture fixture)
{
    // ---- a. the required test: many identical requests at once ----

    [Theory]
    [InlineData(10)]
    [InlineData(25)]
    public async Task Parallel_checkout_same_key(int requests)
    {
        var handler = new FakeTwoCTwoPHandler();
        await using var app = fixture.CreateApp(handler);
        var client = app.CreateClient();
        var key = NewKey();

        var responses = await RunTogether(requests, _ => PostCheckout(client, key, Alice, Earbuds));

        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        var ok = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        Assert.NotEmpty(ok);

        var bodies = await Task.WhenAll(ok.Select(ReadCheckout));
        Assert.Single(bodies.Select(b => b.InvoiceNo).Distinct());
        Assert.Single(bodies.Select(b => b.RedirectUrl).Distinct());
        Assert.NotNull(bodies[0].RedirectUrl);

        Assert.Single(await TransactionsByKey(app, key));
        Assert.Equal(1, handler.PaymentTokenCalls);
        Assert.Empty(await fixture.RedisKeysAsync("lock:*"));
    }

    // ---- b. the database is the final guard, whatever the lock does ----

    public enum LockKind { RealRedis, AlwaysGrants, Throwing }

    [Theory]
    [InlineData(LockKind.RealRedis)]
    [InlineData(LockKind.AlwaysGrants)]
    [InlineData(LockKind.Throwing)]
    public async Task Parallel_checkout_same_key_creates_one_row_whatever_the_lock_does(LockKind lockKind)
    {
        var handler = new FakeTwoCTwoPHandler();
        await using var app = fixture.CreateApp(handler, services =>
        {
            switch (lockKind)
            {
                case LockKind.AlwaysGrants:
                    services.RemoveAll<IDistributedLock>();
                    services.AddSingleton<IDistributedLock, AlwaysGrantsLock>();
                    break;
                case LockKind.Throwing:
                    services.RemoveAll<IDistributedLock>();
                    services.AddSingleton<IDistributedLock, ThrowingLock>();
                    break;
            }
        });
        var client = app.CreateClient();
        var key = NewKey();

        var responses = await RunTogether(10, _ => PostCheckout(client, key, Alice, Earbuds));

        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        var ok = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        Assert.NotEmpty(ok);
        var bodies = await Task.WhenAll(ok.Select(ReadCheckout));
        Assert.Single(bodies.Select(b => b.InvoiceNo).Distinct());

        Assert.Single(await TransactionsByKey(app, key));
        Assert.Equal(1, handler.PaymentTokenCalls);
    }

    // ---- c. sequential replay ----

    [Fact]
    public async Task Sequential_replay_returns_the_same_response_without_calling_2c2p_again()
    {
        var handler = new FakeTwoCTwoPHandler();
        await using var app = fixture.CreateApp(handler);
        var client = app.CreateClient();
        var key = NewKey();

        var first = await PostCheckout(client, key, Alice, Earbuds);
        var second = await PostCheckout(client, key, Alice, Earbuds);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(await ReadCheckout(first), await ReadCheckout(second));
        Assert.Equal(1, handler.PaymentTokenCalls);
    }

    // ---- d. different keys are independent ----

    [Fact]
    public async Task Parallel_checkouts_with_different_keys_are_all_created()
    {
        var handler = new FakeTwoCTwoPHandler();
        await using var app = fixture.CreateApp(handler);
        var client = app.CreateClient();
        var keys = Enumerable.Range(0, 10).Select(_ => NewKey()).ToArray();

        var responses = await RunTogether(10, i => PostCheckout(client, keys[i], Alice, Earbuds));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(ReadCheckout));
        Assert.Equal(10, bodies.Select(b => b.InvoiceNo).Distinct().Count());
        Assert.Equal(10, handler.PaymentTokenCalls);
        foreach (var key in keys)
            Assert.Single(await TransactionsByKey(app, key));
    }

    // ---- e. client errors ----

    [Fact]
    public async Task Same_key_with_a_different_product_is_422()
    {
        await using var app = fixture.CreateApp(new FakeTwoCTwoPHandler());
        var client = app.CreateClient();
        var key = NewKey();

        (await PostCheckout(client, key, Alice, Earbuds)).EnsureSuccessStatusCode();
        var reused = await PostCheckout(client, key, Alice, ChargingDock);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);
    }

    [Fact]
    public async Task Missing_idempotency_key_is_400()
    {
        var handler = new FakeTwoCTwoPHandler();
        await using var app = fixture.CreateApp(handler);

        var response = await PostCheckout(app.CreateClient(), key: null, Alice, Earbuds);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, handler.PaymentTokenCalls);
    }

    [Theory]
    [InlineData(999, Earbuds)]
    [InlineData(Alice, 999)]
    public async Task Unknown_customer_or_product_is_404(int customerId, int productId)
    {
        var handler = new FakeTwoCTwoPHandler();
        await using var app = fixture.CreateApp(handler);

        var response = await PostCheckout(app.CreateClient(), NewKey(), customerId, productId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, handler.PaymentTokenCalls);
    }

    // ---- f. saved card ----

    [Fact]
    public async Task Parallel_saved_card_checkout_same_key_charges_once()
    {
        var handler = new FakeTwoCTwoPHandler();
        await using var app = fixture.CreateApp(handler);
        var client = app.CreateClient();
        var cardId = await CreateSavedCard(app, client, Alice, "CT_" + Guid.NewGuid().ToString("N"));
        var tokenCallsBefore = handler.PaymentTokenCalls;   // the setup checkout made one
        var key = NewKey();

        var responses = await RunTogether(10, _ => PostCheckout(client, key, Alice, Earbuds, cardId));

        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        var ok = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        Assert.NotEmpty(ok);
        var bodies = await Task.WhenAll(ok.Select(ReadCheckout));
        Assert.Single(bodies.Select(b => b.InvoiceNo).Distinct());
        Assert.All(bodies, b =>
        {
            Assert.Equal(nameof(TransactionStatus.Succeeded), b.Status);
            Assert.Null(b.RedirectUrl);
        });

        var tx = Assert.Single(await TransactionsByKey(app, key));
        Assert.Equal(TransactionStatus.Succeeded, tx.Status);
        Assert.Equal(cardId, tx.CustomerCardId);
        Assert.Equal(1, handler.PaymentTokenCalls - tokenCallsBefore);
        Assert.Equal(1, handler.DoPaymentCalls);
        Assert.Equal(1, handler.InquiryCalls);
    }

    [Fact]
    public async Task Saved_card_of_another_customer_is_404()
    {
        var handler = new FakeTwoCTwoPHandler();
        await using var app = fixture.CreateApp(handler);
        var client = app.CreateClient();
        var alicesCard = await CreateSavedCard(app, client, Alice, "CT_" + Guid.NewGuid().ToString("N"));
        var key = NewKey();

        var response = await PostCheckout(client, key, Ben, Earbuds, alicesCard);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await TransactionsByKey(app, key));
        Assert.Equal(0, handler.DoPaymentCalls);
    }
}
