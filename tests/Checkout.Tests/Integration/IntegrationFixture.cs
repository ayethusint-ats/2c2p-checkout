using Checkout.Application.Payments;
using Checkout.Infrastructure.Payments.TwoCTwoP;
using Checkout.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.MySql;
using Testcontainers.Redis;

namespace Checkout.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationFixture>
{
    public const string Name = "Integration";
}

/// <summary>
/// One MySQL and one Redis container for the whole collection (tests in a collection run one at a time).
/// Each test still gets its own app, fake 2C2P handler and GUID keys, so tests don't depend on each other.
/// </summary>
public sealed class IntegrationFixture : IAsyncLifetime
{
    // Made-up test credentials: never the real sandbox values.
    public const string MerchantId = "TESTMERCHANT01";
    public const string SecretKey = "test-secret-key-for-integration-tests-only-0123456789";

    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7").Build();

    public IConnectionMultiplexer Redis { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_mysql.StartAsync(), _redis.StartAsync());
        Redis = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await Redis.DisposeAsync();
        await Task.WhenAll(_mysql.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }

    /// <summary>The API wired to the containers, with 2C2P replaced by <paramref name="handler"/>.</summary>
    internal CheckoutApiFactory CreateApp(FakeTwoCTwoPHandler handler, Action<IServiceCollection>? configureServices = null) =>
        new(Settings(), services =>
        {
            // Same typed client as production, so only the network hop is replaced.
            services.AddHttpClient<IPaymentProvider, TwoCTwoPPaymentProvider>()
                .ConfigurePrimaryHttpMessageHandler(() => handler);
            configureServices?.Invoke(services);
        });

    private Dictionary<string, string?> Settings() => new()
    {
        ["ConnectionStrings:Checkout"] = _mysql.GetConnectionString(),
        ["Redis:ConnectionString"] = _redis.GetConnectionString(),
        ["TwoCTwoP:MerchantId"] = MerchantId,
        ["TwoCTwoP:SecretKey"] = SecretKey,
        ["App:FrontendBaseUrl"] = "http://localhost:3000",
        ["App:PublicApiBaseUrl"] = "http://localhost:5099",
        ["Checkout:LockTtlSeconds"] = "100",
        ["Checkout:WaitTimeoutSeconds"] = "10",
        ["Checkout:PollIntervalMilliseconds"] = "100",
        ["Logging:LogLevel:Default"] = "Warning",
    };

    /// <summary>All Redis keys matching <paramref name="pattern"/> (SCAN, not KEYS).</summary>
    public async Task<List<string>> RedisKeysAsync(string pattern)
    {
        var server = Redis.GetServer(Redis.GetEndPoints()[0]);
        var keys = new List<string>();
        await foreach (var key in server.KeysAsync(pattern: pattern))
            keys.Add(key.ToString());
        return keys;
    }
}

internal sealed class CheckoutApiFactory(
    Dictionary<string, string?> settings,
    Action<IServiceCollection> configureServices) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development": appsettings.Development.json (real secrets) must never be loaded here.
        builder.UseEnvironment("Testing");

        // In-memory settings via UseSetting rather than ConfigureAppConfiguration: Program reads the
        // connection strings while registering services, before ConfigureAppConfiguration would apply.
        foreach (var (key, value) in settings)
            builder.UseSetting(key, value);
        // Runs after Program's registrations, so these replace them.
        builder.ConfigureTestServices(configureServices);
    }

    /// <summary>Runs a query on a fresh DbContext, so it reads what is in MySQL right now.</summary>
    public async Task<T> QueryDbAsync<T>(Func<CheckoutDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<CheckoutDbContext>());
    }
}
