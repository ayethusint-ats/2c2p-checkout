namespace Checkout.Application.Abstractions;

/// <summary>
/// Key/value cache. Implementations may throw when the cache is unavailable;
/// callers decide whether that is fatal.
/// </summary>
public interface ICacheService
{
    /// <returns>The cached value, or null on a miss.</returns>
    Task<T?> GetAsync<T>(string key);

    Task SetAsync<T>(string key, T value, TimeSpan ttl);

    Task RemoveAsync(string key);
}
