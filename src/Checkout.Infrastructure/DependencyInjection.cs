using Checkout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Checkout.Infrastructure;

public static class DependencyInjection
{
    // Fixed instead of ServerVersion.AutoDetect, which opens a DB connection at startup
    // and whenever the `dotnet ef` tools run. Matches mysql:8 (8.4) in docker-compose.yml.
    private static readonly MySqlServerVersion ServerVersion = new(new Version(8, 4, 0));

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<CheckoutDbContext>(options => options
            .UseMySql(connectionString, ServerVersion)
            .UseSeeding((db, _) => CheckoutDbSeeder.Seed((CheckoutDbContext)db))
            .UseAsyncSeeding((db, _, ct) => CheckoutDbSeeder.SeedAsync((CheckoutDbContext)db, ct)));

        return services;
    }

    /// <summary>Applies pending migrations, then runs the seeder (EF calls it as part of Migrate).</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
