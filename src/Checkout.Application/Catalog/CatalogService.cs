using Checkout.Application.Abstractions;
using Checkout.Application.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Checkout.Application.Catalog;

public sealed class CatalogService(
    ICustomerRepository customers,
    IProductRepository products,
    ICustomerCardRepository cards,
    ICacheService cache,
    IOptions<CacheOptions> cacheOptions,
    ILogger<CatalogService> logger)
{
    public const string CustomersCacheKey = "customers:all";
    public const string ProductsCacheKey = "products:all";

    public Task<IReadOnlyList<CustomerDto>> GetCustomersAsync(CancellationToken ct) =>
        GetOrLoadAsync(CustomersCacheKey, customers.GetAllAsync, ct);

    public Task<IReadOnlyList<ProductDto>> GetProductsAsync(CancellationToken ct) =>
        GetOrLoadAsync(ProductsCacheKey, products.GetAllAsync, ct);

    /// <summary>
    /// Not cached: a payment notification can save a new card at any time,
    /// and a stale list would hide it.
    /// </summary>
    /// <returns>The customer's cards, or null if the customer does not exist.</returns>
    public async Task<IReadOnlyList<SavedCardDto>?> GetSavedCardsAsync(int customerId, CancellationToken ct)
    {
        if (!await customers.ExistsAsync(customerId, ct))
            return null;

        return await cards.GetByCustomerIdAsync(customerId, ct);
    }

    // Cache-aside: try the cache, on a miss load from the database and store the result.
    // The cache is an optimisation, so any cache failure is logged and the database is used.
    private async Task<IReadOnlyList<T>> GetOrLoadAsync<T>(
        string key,
        Func<CancellationToken, Task<IReadOnlyList<T>>> load,
        CancellationToken ct)
    {
        try
        {
            var cached = await cache.GetAsync<IReadOnlyList<T>>(key);
            if (cached is not null)
                return cached;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache read failed for {CacheKey}; reading from the database", key);
        }

        var items = await load(ct);

        try
        {
            await cache.SetAsync(key, items, cacheOptions.Value.CatalogTtl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache write failed for {CacheKey}; returning database result uncached", key);
        }

        return items;
    }
}
