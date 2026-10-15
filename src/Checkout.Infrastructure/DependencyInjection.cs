using Checkout.Application.Abstractions;
using Checkout.Infrastructure.Caching;
using Checkout.Infrastructure.Persistence;
using Checkout.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Checkout.Infrastructure;

public static class DependencyInjection
{
    // Fixed instead of ServerVersion.AutoDetect, which opens a DB connection at startup
    // and whenever the `dotnet ef` tools run. Matches mysql:8 (8.4) in docker-compose.yml.
    private static readonly MySqlServerVersion ServerVersion = new(new Version(8, 4, 0));

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        AddPersistence(services, configuration);
        AddRedis(services, configuration);
        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        // Fail fast with a clear message rather than a MySQL connection error later.
        var connectionString = configuration.GetConnectionString("Checkout")
            ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:Checkout' is not configured.");

        services.AddDbContext<CheckoutDbContext>(options => options
            .UseMySql(connectionString, ServerVersion)
            .UseSeeding((db, _) => CheckoutDbSeeder.Seed((CheckoutDbContext)db))
            .UseAsyncSeeding((db, _, ct) => CheckoutDbSeeder.SeedAsync((CheckoutDbContext)db, ct)));

        // Scoped, like the DbContext they wrap: one per HTTP request.
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICustomerCardRepository, CustomerCardRepository>();
    }

    private static void AddRedis(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration["Redis:ConnectionString"]
            ?? throw new InvalidOperationException("'Redis:ConnectionString' is not configured.");

        var options = ConfigurationOptions.Parse(connectionString);

        // Start even if Redis is down; the multiplexer keeps reconnecting in the background.
        options.AbortOnConnectFail = false;

        // While disconnected, fail commands immediately instead of queueing them until the
        // 5 s timeout, so the cache fallback in CatalogService doesn't slow every request.
        options.BacklogPolicy = BacklogPolicy.FailFast;

        // One multiplexer for the whole app: it is thread-safe and designed to be shared.
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));
        services.AddSingleton<ICacheService, RedisCacheService>();
    }

    /// <summary>Applies pending migrations, then runs the seeder (EF calls it as part of Migrate).</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
