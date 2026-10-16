using Checkout.Domain.Entities;
using Checkout.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Checkout.Infrastructure.Persistence;

/// <summary>
/// Demo data. Runs after migrations (wired through UseSeeding/UseAsyncSeeding)
/// and only inserts into empty tables, so it is safe to run on every start.
/// </summary>
internal static class CheckoutDbSeeder
{
    public static async Task SeedAsync(CheckoutDbContext db, CancellationToken ct)
    {
        DisableSavepoints(db);

        if (!await db.Customers.AnyAsync(ct))
            db.Customers.AddRange(Customers());

        if (!await db.Products.AnyAsync(ct))
            db.Products.AddRange(Products());

        await db.SaveChangesAsync(ct);
    }

    // EF also needs a synchronous version for `dotnet ef database update`.
    public static void Seed(CheckoutDbContext db)
    {
        DisableSavepoints(db);

        if (!db.Customers.Any())
            db.Customers.AddRange(Customers());

        if (!db.Products.Any())
            db.Products.AddRange(Products());

        db.SaveChanges();
    }

    // EF runs seeding inside Migrate's transaction, so SaveChanges would create a savepoint.
    // MySQL implicitly commits after DDL (CREATE TABLE...), so that transaction is already
    // gone, the savepoint can't be released, and EF logs a spurious "error using a transaction".
    private static void DisableSavepoints(CheckoutDbContext db) =>
        db.Database.AutoSavepointsEnabled = false;

    private static Customer[] Customers() =>
    [
        new() { FirstName = "Alice", LastName = "Tan", Email = "alice.tan@example.com" },
        new() { FirstName = "Ben", LastName = "Lim", Email = "ben.lim@example.com" },
        new() { FirstName = "Chloe", LastName = "Wong", Email = "chloe.wong@example.com" },
    ];

    private static Product[] Products() =>
    [
        new()
        {
            Name = "Wireless Earbuds",
            Description = "Bluetooth earbuds with charging case.",
            Price = 89.90m, Currency = "SGD", PaymentType = PaymentType.OneTime,
        },
        new()
        {
            Name = "USB-C Charging Dock",
            Description = "Three-port fast charging dock.",
            Price = 39.50m, Currency = "SGD", PaymentType = PaymentType.OneTime,
        },
        new()
        {
            Name = "Cloud Storage Monthly",
            Description = "200 GB cloud storage, billed monthly.",
            Price = 4.99m, Currency = "SGD", PaymentType = PaymentType.Recurring,
        },
        new()
        {
            Name = "Premium Support Monthly",
            Description = "Priority support, billed monthly.",
            Price = 19.00m, Currency = "SGD", PaymentType = PaymentType.Recurring,
        },
    ];
}
